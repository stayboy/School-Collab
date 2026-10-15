using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Landing;
using SchoolCollab.Admin.Shared.Services;
using TenantsIndex = SchoolCollab.Settings.Application.Components.Pages.Tenants.Index;

namespace SchoolCollab.Settings.Tests.Unit.Tenants;

/// <summary>
/// bUnit tests for the <c>/tenants</c> page's default-tenant clear banner (spec
/// <c>documents/specs/tenant-default-dropdown-action.md</c>, FR-7).
/// </summary>
/// <remarks>
/// This is the page's first component-level coverage. The banner is the surface that RELEASES
/// the operator's personal default-tenant preference; setting it lives in the header dropdown.
/// The read is deliberately non-fatal — a preference the portal cannot read must never stop the
/// registry list from rendering — so most of these tests are about degradation, not the happy path.
/// </remarks>
[TestClass]
public class TenantsIndexTests : BunitContext
{
    private static readonly Guid Alpha = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Beta = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string TenantsJson =
        """
        [{"id":"11111111-1111-1111-1111-111111111111","name":"Alpha","type":"School","isDefault":true},
         {"id":"22222222-2222-2222-2222-222222222222","name":"Beta","type":"School","isDefault":false}]
        """;

    private const string NoPreferenceJson = """{"defaultTenantId":null}""";

    public TenantsIndexTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddLogging();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body)> Calls = new();
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new(StringComparer.OrdinalIgnoreCase);

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[$"{method.ToUpperInvariant()} {url}"] = (status, body);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));

            var key = $"{request.Method.Method.ToUpperInvariant()} {request.RequestUri.PathAndQuery}";
            if (_responses.TryGetValue(key, out var hit))
            {
                return new HttpResponseMessage(hit.Status)
                {
                    Content = new StringContent(hit.Body, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private (IRenderedComponent<TenantsIndex> Cut, ScriptedHandler Handler) Render(
        string? preferenceJson = null,
        HttpStatusCode preferenceStatus = HttpStatusCode.OK,
        HttpStatusCode clearStatus = HttpStatusCode.NoContent)
    {
        var handler = new ScriptedHandler()
            .Map("GET", "/api/tenants", HttpStatusCode.OK, TenantsJson)
            .Map("GET", "/api/tenants/me/default", preferenceStatus, preferenceJson ?? NoPreferenceJson)
            .Map("PUT", "/api/tenants/me/default", clearStatus, string.Empty);

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://settings-api") };
        Services.AddSingleton(new TenantsApiClient(http));

        return (Render<TenantsIndex>(), handler);
    }

    private static LandingPage<TenantDto> Landing(IRenderedComponent<TenantsIndex> cut)
        => cut.FindComponent<LandingPage<TenantDto>>().Instance;

    private static void ClickClear(IRenderedComponent<TenantsIndex> cut)
        => cut.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Clear").Click();

    private static IEnumerable<(string Method, string Url, string? Body)> ClearCalls(ScriptedHandler handler)
        => handler.Calls.Where(c => c.Method == "PUT" && c.Url == "/api/tenants/me/default");

    [TestMethod]
    public void Banner_IsHidden_WhenNoPreferenceIsRecorded()
    {
        var (cut, _) = Render();

        cut.Markup.Should().NotContain("Your default tenant");
        cut.FindAll("fluent-button").Should().NotContain(
            b => b.TextContent.Trim() == "Clear", "there is nothing to release without a recorded preference");
    }

    [TestMethod]
    public void Banner_NamesTheTenant_WhenAPreferenceIsRecorded()
    {
        var (cut, _) = Render($$"""{"defaultTenantId":"{{Alpha}}"}""");

        cut.Markup.Should().Contain($"Your default tenant: Alpha");
        cut.FindAll("fluent-button").Should().Contain(
            b => b.TextContent.Trim() == "Clear", "a recorded preference must be releasable from here");
    }

    /// <summary>
    /// <see cref="Guid.Empty"/> is an explicit preference for the system / no-tenant context —
    /// a real, recorded value that must render as such, not as "no preference".
    /// </summary>
    [TestMethod]
    public void Banner_ShowsTheSystemLabel_WhenThePreferenceIsTheSystemContext()
    {
        var (cut, _) = Render($$"""{"defaultTenantId":"{{Guid.Empty}}"}""");

        cut.Markup.Should().Contain("Your default tenant: system / no tenant");
    }

    /// <summary>
    /// A preference for a tenant the registry no longer returns still renders — falling back to
    /// the raw id — so the operator can still release it instead of being stuck with a banner
    /// that never appears.
    /// </summary>
    [TestMethod]
    public void Banner_ShowsTheRawId_WhenThePreferredTenantIsNotInTheRegistry()
    {
        var unknown = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var (cut, _) = Render($$"""{"defaultTenantId":"{{unknown}}"}""");

        cut.Markup.Should().Contain($"Your default tenant: {unknown}");
    }

    [TestMethod]
    public void Clear_WritesNull_AndHidesTheBanner_LeavingTheGridUntouched()
    {
        var (cut, handler) = Render($$"""{"defaultTenantId":"{{Alpha}}"}""");

        ClickClear(cut);

        ClearCalls(handler).Should().ContainSingle();
        ClearCalls(handler).Single().Body.Should().Contain(
            "\"tenantId\":null", "clearing releases the preference; it must not set a new one");

        cut.Markup.Should().NotContain("Your default tenant", "the banner disappears once the preference is gone");
        Landing(cut).Items.Should().HaveCount(2, "releasing the preference does not touch the registry grid");
    }

    /// <summary>
    /// The whole point of reading the preference separately: a portal that cannot read it must
    /// still list the tenants. Before the parallel read, a failed preference read would have taken
    /// the registry list down with it.
    /// </summary>
    [TestMethod]
    public void FailedPreferenceRead_DegradesToNoBanner_AndKeepsTheGrid()
    {
        var (cut, _) = Render(preferenceStatus: HttpStatusCode.InternalServerError);

        cut.Markup.Should().NotContain("Your default tenant", "an unreadable preference degrades to 'none recorded'");
        Landing(cut).Items.Should().HaveCount(2, "the registry list must survive a failed preference read");
        Landing(cut).Error.Should().BeNullOrEmpty(
            "an unreadable preference is not a page error — the registry loaded fine");
    }

    [TestMethod]
    public void FailedClear_SurfacesTheError_AndKeepsTheBanner()
    {
        var (cut, _) = Render(
            $$"""{"defaultTenantId":"{{Alpha}}"}""", clearStatus: HttpStatusCode.InternalServerError);

        ClickClear(cut);

        Landing(cut).Error.Should().NotBeNullOrEmpty(
            "a failed clear surfaces on the shared error strip, like MakeDefaultAsync");
        cut.Markup.Should().Contain("Your default tenant", "the operator can retry while the banner is still there");
    }
}
