using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Landing;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Auth;
using TenantsIndex = SchoolCollab.Settings.Application.Components.Pages.Tenants.Index;

namespace SchoolCollab.Settings.Tests.Unit.Tenants;

/// <summary>
/// bUnit tests for the <c>/tenants</c> page's default-tenant surfaces: the clear banner for the
/// operator's recorded preference (spec <c>documents/specs/tenant-default-dropdown-action.md</c>,
/// FR-7) and the dev session-pin line for the header switcher's explicit pick (B7).
/// </summary>
/// <remarks>
/// This is the page's first component-level coverage. The banner RELEASES the operator's personal
/// default-tenant preference; the pin line releases the dev session's explicit pick.
/// <para>
/// The preference read is deliberately non-fatal — a preference the portal cannot read must never
/// stop the registry list from rendering — so most of these tests are about degradation, not the
/// happy path.
/// </para>
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

    /// <summary>The registry with no platform default designated — every "Platform default" cell is a dash.</summary>
    private const string NoDefaultTenantsJson =
        """
        [{"id":"11111111-1111-1111-1111-111111111111","name":"Alpha","type":"School","isDefault":false},
         {"id":"22222222-2222-2222-2222-222222222222","name":"Beta","type":"School","isDefault":false}]
        """;

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

    /// <summary>
    /// The dev tenant switcher's explicit pick, stubbed: the page reads the selection slot to
    /// decide whether the session-pin line renders, and writes it to release the pick (B7).
    /// </summary>
    private sealed class StubSelection : IDevTenantSelection
    {
        /// <summary>The pick currently in force, or null when there is none.</summary>
        public Guid? Selected { get; set; }

        /// <summary>When true every write throws — the surface of a portal whose store is unreachable.</summary>
        public bool FailWrites { get; set; }

        public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default)
            => Task.FromResult(Selected);

        public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
        {
            if (FailWrites)
            {
                return Task.FromException(new InvalidOperationException("the dev tenant selection store is unreachable"));
            }

            Selected = tenantId;
            return Task.CompletedTask;
        }

        public Task<Guid?> GetAppliedDefaultTenantIdAsync(CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);

        public Task SetAppliedDefaultTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Records what the page asked the navigation manager for. The page reloads the current URI
    /// (so <c>Uri</c> itself never changes), which is why the reload needs an explicit spy rather
    /// than a URI assertion.
    /// </summary>
    /// <remarks>
    /// The overridden seam is the <see cref="NavigationOptions"/> overload on purpose:
    /// <see cref="NavigationManager.Refresh(bool)"/> routes through
    /// <c>NavigateToCore(Uri, new NavigationOptions { ForceLoad = true, ReplaceHistoryEntry = true })</c>,
    /// which a spy overriding only <c>NavigateToCore(string, bool)</c> would never see.
    /// </remarks>
    private sealed class RecordingNavigationManager : NavigationManager
    {
        /// <summary>Every navigation the page requested, with the forced-reload flag it carried.</summary>
        public List<(string Uri, bool ForceLoad)> Requests { get; } = [];

        /// <summary>The URIs of the requests that asked for a hard document load.</summary>
        public List<string> ForcedReloads { get; } = [];

        public RecordingNavigationManager() => Initialize("http://localhost/tenants", "http://localhost/tenants");

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Requests.Add((uri, options.ForceLoad));

            if (options.ForceLoad)
            {
                ForcedReloads.Add(uri);
            }
        }
    }

    /// <summary>The navigation spy registered for the current <see cref="Render"/> and its assertions.</summary>
    private RecordingNavigationManager Nav = null!;

    private (IRenderedComponent<TenantsIndex> Cut, ScriptedHandler Handler) Render(
        string? preferenceJson = null,
        HttpStatusCode preferenceStatus = HttpStatusCode.OK,
        HttpStatusCode clearStatus = HttpStatusCode.NoContent,
        Guid? pick = null,
        bool failPickWrite = false,
        string? tenantsJson = null)
    {
        var handler = new ScriptedHandler()
            .Map("GET", "/api/tenants", HttpStatusCode.OK, tenantsJson ?? TenantsJson)
            .Map("GET", "/api/tenants/me/default", preferenceStatus, preferenceJson ?? NoPreferenceJson)
            .Map("PUT", "/api/tenants/me/default", clearStatus, string.Empty);

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://settings-api") };
        Services.AddSingleton(new TenantsApiClient(http));
        Services.AddSingleton<IDevTenantSelection>(new StubSelection { Selected = pick, FailWrites = failPickWrite });

        // Registered last, so the page's injected NavigationManager is this recording spy.
        Nav = new RecordingNavigationManager();
        Services.AddSingleton<NavigationManager>(Nav);

        return (Render<TenantsIndex>(), handler);
    }

    /// <summary>The stub selection registered for the current <see cref="Render"/> and its assertions.</summary>
    private StubSelection Selection()
        => (StubSelection)Services.GetRequiredService<IDevTenantSelection>();

    private static LandingPage<TenantDto> Landing(IRenderedComponent<TenantsIndex> cut)
        => cut.FindComponent<LandingPage<TenantDto>>().Instance;

    private static void ClickClear(IRenderedComponent<TenantsIndex> cut)
        => cut.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Clear").Click();

    private static void ClickFollowPortalDefault(IRenderedComponent<TenantsIndex> cut)
        => cut.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Follow the portal default").Click();

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

    // ── B7: the dev session-pin line ─────────────────────────────────────────

    [TestMethod]
    public void Pin_IsHidden_WhenNoExplicitPick()
    {
        var (cut, _) = Render();

        cut.Markup.Should().NotContain("Pinned this session");
        cut.FindAll("fluent-button").Should().NotContain(
            b => b.TextContent.Trim() == "Follow the portal default", "there is no pick to release");
    }

    [TestMethod]
    public void Pin_NamesThePinnedTenant_AndOffersFollowThePortalDefault()
    {
        var (cut, _) = Render($$"""{"defaultTenantId":"{{Beta}}"}""", pick: Alpha);

        cut.Markup.Should().Contain("Pinned this session: Alpha", "the pick is named from the loaded registry");
        cut.Markup.Should().Contain(
            "Your default tenant: Beta", "the two lines are independent — the pick is not the preference");
        cut.FindAll("fluent-button").Should().Contain(b => b.TextContent.Trim() == "Follow the portal default");
    }

    /// <summary>
    /// The reachability case that motivated the pin line: a session pick with no preference
    /// recorded — where the banner is absent — still has to be releasable.
    /// </summary>
    [TestMethod]
    public void Pin_IsShown_EvenWithNoPreferenceRecorded()
    {
        var (cut, _) = Render(pick: Alpha);

        cut.Markup.Should().Contain("Pinned this session: Alpha");
        cut.Markup.Should().NotContain("Your default tenant", "no preference is recorded, so the banner is absent");
    }

    /// <summary>
    /// The release writes the SELECTION slot — the one the header dropdown's row click writes —
    /// and then reloads so the change takes effect in this circuit. The preference is a different
    /// slot with its own release (the banner), and must not be touched here.
    /// </summary>
    [TestMethod]
    public void FollowThePortalDefault_ClearsThePick_AndReloads()
    {
        var (cut, handler) = Render(pick: Alpha);
        var selection = Selection();

        ClickFollowPortalDefault(cut);

        selection.Selected.Should().BeNull("the pick is released, so the portal follows its resolved default again");
        ClearCalls(handler).Should().BeEmpty("releasing the pick must not release the recorded preference");
        Nav.ForcedReloads.Should().ContainSingle("the released pick has to take effect in this circuit");
        cut.Markup.Should().NotContain("Pinned this session", "the pin line disappears with the pick");
    }

    /// <summary>
    /// The release is applied by a hard document reload, so the circuit's claim is recomputed from
    /// the store. A merged HTML refresh (<c>Refresh(forceReload: false)</c>) would leave the claim
    /// stale — exactly the defect the forced reload exists to fix.
    /// </summary>
    [TestMethod]
    public void FollowThePortalDefault_RequestsAForcedReload_OfTheCurrentUri()
    {
        var (cut, _) = Render(pick: Alpha);

        ClickFollowPortalDefault(cut);

        Nav.Requests.Should().ContainSingle("the release is applied by exactly one request");

        var (uri, forceLoad) = Nav.Requests[0];
        uri.Should().Be(Nav.Uri, "the current URI is reloaded, not somewhere else");
        forceLoad.Should().BeTrue(
            "a merged HTML refresh — Refresh(forceReload: false) — would leave this circuit's tenant claim stale");
    }

    [TestMethod]
    public void FailedFollowThePortalDefault_SurfacesTheError_AndKeepsThePin()
    {
        var (cut, _) = Render(pick: Alpha, failPickWrite: true);

        ClickFollowPortalDefault(cut);

        Landing(cut).Error.Should().NotBeNullOrEmpty("a failed release surfaces on the shared error strip");
        cut.Markup.Should().Contain("Pinned this session", "the pick is still in force, so the operator can retry");
    }

    // ── Q10: the two default columns, their tooltips, and the empty-state note ──────────────

    /// <summary>
    /// The two defaults are different concepts, so each gets a column: the one shared
    /// designation, and the operator's own recorded preference. The old "Portal default" header
    /// named neither clearly and is gone.
    /// </summary>
    [TestMethod]
    public void Grid_NamesBothDefaultColumns()
    {
        var (cut, _) = Render();

        var headers = cut.FindAll("th").Select(h => h.TextContent.Trim()).ToArray();

        headers.Should().Contain("Platform default");
        headers.Should().Contain("Your default");
        headers.Should().NotContain("Portal default");
    }

    /// <summary>
    /// The columns are only self-explaining if the tooltips actually render. With the FluentUI
    /// grid that takes the column's <c>Tooltip</c> switch as well as <c>HeaderTooltip</c> —
    /// <c>Tooltip</c> defaults false and gates the tooltip, so a header that carries only
    /// <c>HeaderTooltip</c> renders none.
    /// </summary>
    [TestMethod]
    public void Grid_HeaderTooltips_ExplainTheTwoDefaults()
    {
        var (cut, _) = Render();

        cut.Markup.Should().Contain("wins over the platform default",
            "the personal column says how it relates to the shared one");
        cut.Markup.Should().Contain("when the operator has no personal default",
            "the platform column says when it applies");
    }

    [TestMethod]
    public void Grid_BadgesTheDesignatedPlatformDefault_AndOmitsTheEmptyStateNote()
    {
        var (cut, _) = Render();

        cut.FindAll("fluent-badge").Should().Contain(b => b.TextContent.Trim() == "Default");
        cut.Markup.Should().NotContain("No platform default is designated yet",
            "the note is only for the state where every Platform default cell is a dash");
    }

    /// <summary>
    /// The state that started the bug report: no tenant is designated, so every "Platform default"
    /// cell is a dash. The page now says why instead of leaving a column of dashes.
    /// </summary>
    [TestMethod]
    public void Grid_NotesTheEmptyPlatformDefault_WhenNoneIsDesignated()
    {
        var (cut, _) = Render(tenantsJson: NoDefaultTenantsJson);

        cut.Markup.Should().Contain("No platform default is designated yet");
        cut.FindAll("fluent-badge").Should().NotContain(b => b.TextContent.Trim() == "Default");
    }

    [TestMethod]
    public void Grid_BadgesTheOperatorsOwnDefault()
    {
        var (cut, _) = Render($$"""{"defaultTenantId":"{{Alpha}}"}""");

        cut.FindAll("fluent-badge").Should().Contain(b => b.TextContent.Trim() == "Yours",
            "the recorded preference is named in the operator's own column");
    }

    [TestMethod]
    public void Grid_ShowsNoYoursBadge_WhenNoPreferenceIsRecorded()
    {
        var (cut, _) = Render();

        cut.FindAll("fluent-badge").Should().NotContain(b => b.TextContent.Trim() == "Yours");
    }
}
