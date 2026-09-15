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
/// bUnit tests for the tenant organization AI prompt page
/// (AssignmentAiPrompt.razor, WS-B2 / spec §3.4). Covers the empty state, the
/// upsert save flow (prompt + lock together), server-side validation surfacing,
/// and the lock switch binding.
/// </summary>
[TestClass]
public class AssignmentAiPromptTests : BunitContext
{
    public AssignmentAiPromptTests()
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

    private static string RowJson(string? systemPrompt, bool isLocked) =>
        JsonSerializer.Serialize(new { systemPrompt, isLocked });

    private ScriptedHandler Register(
        HttpStatusCode getStatus = HttpStatusCode.NoContent,
        string? existingPrompt = null,
        bool existingLocked = false,
        HttpStatusCode putStatus = HttpStatusCode.OK,
        string putBody = "{}")
    {
        var handler = new ScriptedHandler();
        handler.Map(
            "GET",
            "/api/settings/assignment-ai-prompt",
            getStatus,
            getStatus == HttpStatusCode.OK ? RowJson(existingPrompt, existingLocked) : "");
        handler.Map("PUT", "/api/settings/assignment-ai-prompt", putStatus, putBody);

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton(new AssignmentAiPromptApiClient(http));
        Services.AddSingleton<ILogger<AssignmentAiPrompt>>(NullLogger<AssignmentAiPrompt>.Instance);

        return handler;
    }

    private IRenderedComponent<AssignmentAiPrompt> RenderPage() => Render<AssignmentAiPrompt>();

    [TestMethod]
    public void Renders_EmptyState_WhenNoRow()
    {
        Register();

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("built-in default prompt"));

        cut.Markup.Should().Contain("currently in use", "no tenant row means the empty-state hint shows");
    }

    [TestMethod]
    public async Task Save_UpsertsPromptAndLock()
    {
        var handler = Register(
            putStatus: HttpStatusCode.OK,
            putBody: RowJson("District curriculum prompt.", isLocked: true));

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Save"));

        var area = cut.FindComponent<FluentTextArea>();
        await cut.InvokeAsync(() => area.Instance.ValueChanged.InvokeAsync("District curriculum prompt."));
        var toggle = cut.FindComponent<FluentSwitch>();
        await cut.InvokeAsync(() => toggle.Instance.ValueChanged.InvokeAsync(true));

        var button = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Save"));
        await cut.InvokeAsync(() => button.Click());

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Organization AI prompt saved."));
        handler.Calls.Should().Contain(c =>
            c.Method == "PUT" &&
            c.Url == "/api/settings/assignment-ai-prompt" &&
            c.Body == "{\"systemPrompt\":\"District curriculum prompt.\",\"isLocked\":true}");
    }

    [TestMethod]
    public async Task Save_SurfacesValidationError()
    {
        Register(
            putStatus: HttpStatusCode.BadRequest,
            putBody: "{\"error\":\"The organization AI prompt must be 4000 characters or fewer.\"}");

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Save"));

        var area = cut.FindComponent<FluentTextArea>();
        await cut.InvokeAsync(() => area.Instance.ValueChanged.InvokeAsync(new string('x', 4001)));
        var button = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Save"));
        await cut.InvokeAsync(() => button.Click());

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("The organization AI prompt must be 4000 characters or fewer."));
        cut.Markup.Should().NotContain("Organization AI prompt saved.");
    }

    [TestMethod]
    public void LockSwitch_Binds()
    {
        Register(getStatus: HttpStatusCode.OK, existingPrompt: "Existing prompt.", existingLocked: true);

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("locked", "the locked hint renders when the tenant row is locked"));

        cut.FindComponent<FluentSwitch>().Instance.Value.Should().BeTrue("the GET row's IsLocked pre-loads the switch");
    }
}
