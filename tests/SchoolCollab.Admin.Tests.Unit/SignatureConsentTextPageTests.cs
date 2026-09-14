using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Settings.Application.Components.Pages;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the tenant sign-off consent-text page
/// (SignatureConsentText.razor, WS-C2 / spec §3.2). Covers the empty state,
/// the upsert save flow, server-side validation surfacing, and the
/// embedded-default hint.
/// </summary>
[TestClass]
public class SignatureConsentTextTests : BunitContext
{
    public SignatureConsentTextTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body)> Calls = new();
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));
            var key = (request.Method.Method.ToUpperInvariant(), request.RequestUri.PathAndQuery);
            if (_responses.TryGetValue(key, out var hit))
                return new HttpResponseMessage(hit.Status) { Content = new StringContent(hit.Body, Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {request.Method.Method} {request.RequestUri.PathAndQuery}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private static string RowJson(string? consentText) =>
        JsonSerializer.Serialize(new { consentText });

    private (ScriptedHandler Handler, HttpClient Http) Register(
        HttpStatusCode getStatus = HttpStatusCode.NoContent,
        string? existingRow = null,
        HttpStatusCode putStatus = HttpStatusCode.OK,
        string putBody = "{}")
    {
        var handler = new ScriptedHandler();
        handler.Map(
            "GET",
            "/api/settings/signature-consent-text",
            getStatus,
            getStatus == HttpStatusCode.OK ? RowJson(existingRow) : "");
        handler.Map("PUT", "/api/settings/signature-consent-text", putStatus, putBody);

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton(new SignatureConsentTextApiClient(http));
        Services.AddSingleton<ILogger<SignatureConsentText>>(NullLogger<SignatureConsentText>.Instance);

        return (handler, http);
    }

    private IRenderedComponent<SignatureConsentText> RenderPage() => Render<SignatureConsentText>();

    [TestMethod]
    public void Renders_EmptyState_WhenNoRow()
    {
        Register();

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("built-in default consent text"));

        cut.Markup.Should().Contain("currently in use", "no tenant row means the empty-state hint shows");
    }

    [TestMethod]
    public void ShowsEmbeddedDefaultHint()
    {
        Register();

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("built-in default consent text"));

        cut.Markup.Should().Contain("reviewed my ward's completed work", "the embedded default text is rendered in the hint");
    }

    [TestMethod]
    public async Task Save_UpsertsConsentText()
    {
        var (handler, _) = Register(putStatus: HttpStatusCode.OK, putBody: RowJson("My tenant consent."));

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Save"));
        cut.Markup.Should().NotContain("Something went wrong");

        var area = cut.FindComponent<FluentTextArea>();
        await cut.InvokeAsync(() => area.Instance.ValueChanged.InvokeAsync("My tenant consent."));
        var button = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Save"));
        await cut.InvokeAsync(() => button.Click());

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Consent text saved."));
        handler.Calls.Should().Contain(c =>
            c.Method == "PUT" &&
            c.Url == "/api/settings/signature-consent-text" &&
            c.Body == "{\"consentText\":\"My tenant consent.\"}");
    }

    [TestMethod]
    public async Task Save_SurfacesValidationError()
    {
        Register(
            putStatus: HttpStatusCode.BadRequest,
            putBody: "{\"error\":\"Consent text must be 4000 characters or fewer.\"}");

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Save"));

        var area = cut.FindComponent<FluentTextArea>();
        await cut.InvokeAsync(() => area.Instance.ValueChanged.InvokeAsync(new string('x', 4001)));
        var button = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Save"));
        await cut.InvokeAsync(() => button.Click());

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Consent text must be 4000 characters or fewer."));
        cut.Markup.Should().NotContain("Consent text saved.");
    }
}
