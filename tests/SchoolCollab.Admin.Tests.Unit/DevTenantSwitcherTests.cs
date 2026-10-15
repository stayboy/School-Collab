using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Admin.Components.Layout;
using SchoolCollab.Admin.Services;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The dev tenant switcher's three-state dropdown and its self-service default-tenant action.
/// </summary>
/// <remarks>
/// <para>
/// The state that is easy to get subtly wrong is "nothing picked": the select then shows the row
/// of the tenant the portal resolved and is operating under, but that is display-only — it must
/// never be written to the selection slot, or "no explicit pick" and "the operator chose this
/// tenant" would collapse into one state. Each state therefore gets a test that asserts the
/// <em>resolved selection</em>, not just the rendered labels, plus one that asserts the selection
/// slot is still empty after a no-pick load.
/// </para>
/// <para>
/// The self-service action (spec <c>documents/specs/tenant-default-dropdown-action.md</c>) lives
/// INSIDE the option list: a decorative disabled separator plus an enabled <c>Set as default</c>
/// entry that is never allowed to become the bound selection. These tests pin the action zone's
/// visibility table (FR-3), the "never selected" invariant (FR-6), and the confirm/cancel
/// contract (FR-4/FR-5) — including that cancel writes nothing.
/// </para>
/// </remarks>
[TestClass]
public class DevTenantSwitcherTests : BunitContext
{
    private static readonly Guid Alpha = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Beta = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string SeparatorLabel = "──── my default ────";
    private const string ActionLabel = "Set as default";

    private const string TenantsJson =
        """
        [{"id":"11111111-1111-1111-1111-111111111111","name":"Alpha","type":"School","isDefault":true},
         {"id":"22222222-2222-2222-2222-222222222222","name":"Beta","type":"School","isDefault":false}]
        """;

    /// <summary>The same registry with no tenant designated as the platform default.</summary>
    private const string NoDefaultTenantsJson =
        """
        [{"id":"11111111-1111-1111-1111-111111111111","name":"Alpha","type":"School","isDefault":false},
         {"id":"22222222-2222-2222-2222-222222222222","name":"Beta","type":"School","isDefault":false}]
        """;

    public DevTenantSwitcherTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddLogging();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body)> Calls = new();
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new(StringComparer.OrdinalIgnoreCase);
        private readonly Task? _gate;

        /// <param name="gate">
        /// When supplied, every request waits on it before answering. Lets a test hold the load
        /// open so it can observe the component's pre-resolution render — the production timing
        /// where <c>_effective</c> is still null on the first render.
        /// </param>
        public ScriptedHandler(Task? gate = null) => _gate = gate;

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[$"{method.ToUpperInvariant()} {url}"] = (status, body);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_gate is not null)
            {
                await _gate.WaitAsync(cancellationToken);
            }

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

    private sealed class StubSelection : IDevTenantSelection
    {
        public Guid? Selected { get; set; }
        public Guid? Applied { get; private set; }

        public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default) => Task.FromResult(Selected);
        public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
        {
            Selected = tenantId;
            return Task.CompletedTask;
        }

        public Task<Guid?> GetAppliedDefaultTenantIdAsync(CancellationToken ct = default) => Task.FromResult(Applied);
        public Task SetAppliedDefaultTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
        {
            Applied = tenantId;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuthStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(user));
    }

    /// <summary>
    /// Records the forced reloads the component asks for. The switcher reloads the current URI
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
        public List<string> ForcedReloads { get; } = [];

        public RecordingNavigationManager() => Initialize("http://localhost/", "http://localhost/");

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            if (options.ForceLoad)
            {
                ForcedReloads.Add(uri);
            }
        }
    }

    /// <summary>The navigation spy registered for the current <see cref="Render"/> and its assertions.</summary>
    private RecordingNavigationManager Nav = null!;

    /// <summary>
    /// A principal carrying (or omitting) the <c>tenant_id</c> claim, mirroring what
    /// <see cref="TestAuthHandler"/> produces: a real tenant id, or no claim at all for the
    /// system / no-tenant context.
    /// </summary>
    private static ClaimsPrincipal UserWithTenant(Guid? tenantId)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "Test User") };
        if (tenantId is { } id && id != Guid.Empty)
            claims.Add(new Claim("tenant_id", id.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, TestAuthExtensions.TestAuthScheme));
    }

    /// <summary>
    /// Mocks the confirm dialog (<c>ShowConfirmDialogAsync</c> resolves to
    /// <c>ShowDialogAsync&lt;ConfirmDialog, ConfirmDialogContent&gt;</c>) at a fixed outcome, and
    /// records the content it was handed so a test can pin the wording the operator reads.
    /// Registered AFTER <c>AddFluentUIComponents()</c>, so it wins resolution.
    /// </summary>
    private void RegisterConfirmDialog(bool confirm, List<ConfirmDialogContent>? captured = null)
    {
        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(
            confirm ? DialogResult.Ok<object?>(null) : DialogResult.Cancel()));

        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()))
            .Callback<ConfirmDialogContent, DialogParameters>((content, _) => captured?.Add(content))
            .ReturnsAsync(dialogRef.Object);

        Services.AddSingleton(dialogMock.Object);
    }

    private (IRenderedComponent<DevTenantSwitcher> Cut,
             IRenderedComponent<FluentDialogProvider>? Dialogs,
             StubSelection Selection,
             ScriptedHandler Handler) Render(
        Guid? selected,
        Guid? preference = null,
        Guid? claimTenant = null,
        HttpStatusCode putStatus = HttpStatusCode.NoContent,
        bool? confirmDialog = null,
        string? tenantsJson = null,
        Task? gate = null)
    {
        var preferenceJson = preference is null
            ? """{"defaultTenantId":null}"""
            : $$"""{"defaultTenantId":"{{preference}}"}""";

        var handler = new ScriptedHandler(gate)
            .Map("GET", "/api/tenants", HttpStatusCode.OK, tenantsJson ?? TenantsJson)
            .Map("GET", "/api/tenants/me/default", HttpStatusCode.OK, preferenceJson)
            .Map("PUT", "/api/tenants/me/default", putStatus, string.Empty);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:FEATURE:DisableOIDCAuth"] = "true",
            })
            .Build();

        var selection = new StubSelection { Selected = selected };
        var client = new TenantsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://settings-api") });

        Services.AddSingleton<IConfiguration>(config);
        Services.AddSingleton(client);
        Services.AddSingleton<IDevTenantSelection>(selection);
        Services.AddSingleton(new AdminDefaultTenantResolver(
            client, selection, NullLogger<AdminDefaultTenantResolver>.Instance));

        // The component reconciles this circuit's tenant claim against the applied default after
        // the first render, so it needs the auth state the real host supplies.
        Services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider(UserWithTenant(claimTenant)));
        Services.AddSingleton<VisibleTenantService>();

        // Registered last, so the component's injected NavigationManager is this recording spy.
        Nav = new RecordingNavigationManager();
        Services.AddSingleton<NavigationManager>(Nav);

        // The confirm dialog is either driven through its real provider (the end-to-end harness
        // the spec asks for on the cancel path) or mocked to a fixed outcome (the sanctioned
        // write-back harness, used to assert what a confirm actually writes).
        IRenderedComponent<FluentDialogProvider>? dialogs = null;
        if (confirmDialog is { } confirm)
        {
            RegisterConfirmDialog(confirm);
        }
        else
        {
            dialogs = Render<FluentDialogProvider>();
        }

        return (Render<DevTenantSwitcher>(), dialogs, selection, handler);
    }

    private static IRenderedComponent<FluentSelect<DevTenantSwitcher.TenantOption>> SelectOf(
        IRenderedComponent<DevTenantSwitcher> cut)
        => cut.FindComponent<FluentSelect<DevTenantSwitcher.TenantOption>>();

    private static List<string> LabelsOf(IRenderedComponent<DevTenantSwitcher> cut)
        => cut.FindAll("fluent-option").Select(o => o.TextContent.Trim()).ToList();

    /// <summary>The rendered option element whose label is exactly <paramref name="label"/>.</summary>
    private static IElement OptionOf(IRenderedComponent<DevTenantSwitcher> cut, string label)
        => cut.FindAll("fluent-option").First(o => o.TextContent.Trim() == label);

    private static bool HasActionZone(IRenderedComponent<DevTenantSwitcher> cut)
        => LabelsOf(cut).Contains(ActionLabel);

    private static IEnumerable<(string Method, string Url, string? Body)> PutCalls(ScriptedHandler handler)
        => handler.Calls.Where(c => c.Method == "PUT" && c.Url == "/api/tenants/me/default");

    [TestMethod]
    public void RendersSystemAndEveryTenant_WithNoDefaultPseudoEntry()
    {
        // The preference is pinned to the resolved default so the action zone (B5) stays hidden:
        // this test is about the option list itself, not about the zone's entries.
        var (cut, _, _, _) = Render(selected: null, preference: Alpha);

        var labels = LabelsOf(cut);

        labels.Should().HaveCount(3);
        labels[0].Should().Be("(system / no tenant)");
        labels[1].Should().Be("Alpha", "the resolved default is shown as its own row, not as a pseudo-entry of its own");
        labels[2].Should().Be("Beta");
        labels.Should().NotContain(label => label.Contains("default"),
            "the pseudo-entry that named the resolved default is gone — the default's own row is selected instead");
    }

    [TestMethod]
    public void WithNoExplicitPick_SelectsTheResolvedDefaultTenantsRow()
    {
        var (cut, _, selection, _) = Render(selected: null);

        SelectOf(cut).Instance.SelectedOption!.Value.Should().Be(
            Alpha.ToString(), "the control's selected value is the tenant the portal resolved and is operating under");
        selection.Selected.Should().BeNull(
            "showing the resolved default is display-only — only a pick by the operator writes the selection slot");
    }

    /// <summary>
    /// The state that must not collapse into "nothing picked": an explicit system /
    /// no-tenant selection is a real value (<see cref="Guid.Empty"/>), and the dropdown has to
    /// show it as such or the operator can never tell which context they are editing in.
    /// </summary>
    [TestMethod]
    public void WithAnExplicitSystemPick_SelectsTheSystemEntry()
    {
        var (cut, _, _, _) = Render(selected: Guid.Empty);

        SelectOf(cut).Instance.SelectedOption!.Value.Should().Be("system");
    }

    [TestMethod]
    public void WithAnExplicitTenantPick_SelectsThatTenant()
    {
        var (cut, _, _, _) = Render(selected: Beta);

        SelectOf(cut).Instance.SelectedOption!.Value.Should().Be(Beta.ToString());
    }

    /// <summary>
    /// With a recorded preference the resolved default is the PREFERENCE, not the platform
    /// default — the whole point of the second link in the chain — so the select shows the
    /// preferred tenant's own row.
    /// </summary>
    [TestMethod]
    public void WithAPreference_SelectsThePreferredTenantsRow()
    {
        var (cut, _, _, _) = Render(selected: null, preference: Beta);

        SelectOf(cut).Instance.SelectedOption!.Value.Should().Be(Beta.ToString());
    }

    /// <summary>
    /// Nothing designated anywhere: the select falls back to the system / no-tenant row rather
    /// than leaving its selection blank (B2).
    /// </summary>
    [TestMethod]
    public void WithNoDefaultDesignated_SelectsTheSystemRow()
    {
        var (cut, _, _, _) = Render(selected: null, tenantsJson: NoDefaultTenantsJson);

        SelectOf(cut).Instance.SelectedOption!.Value.Should().Be("system");
    }

    /// <summary>
    /// A resolved default the registry no longer lists has no row to select: the select falls back
    /// to the system row instead of leaving <c>SelectedOption</c> null (B2).
    /// </summary>
    [TestMethod]
    public void WithAResolvedDefaultNotInTheRegistry_SelectsTheSystemRow()
    {
        var (cut, _, _, _) = Render(
            selected: null,
            preference: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            tenantsJson: NoDefaultTenantsJson);

        SelectOf(cut).Instance.SelectedOption!.Value.Should().Be("system");
    }

    // ── FR-2 / FR-3: the action zone ─────────────────────────────────────────

    /// <summary>
    /// The intended consequence of selecting the resolved default's own row: on a no-pick load the
    /// selection is a real tenant, so <c>Set as default</c> is reachable without re-picking the
    /// tenant first (B5).
    /// </summary>
    [TestMethod]
    public void ActionZone_IsShown_OnANoPickLoad_WhenTheResolvedDefaultDiffersFromThePreference()
    {
        var (cut, _, _, _) = Render(selected: null, preference: null);

        SelectOf(cut).Instance.SelectedOption!.Value.Should().Be(Alpha.ToString());
        HasActionZone(cut).Should().BeTrue();
        LabelsOf(cut).Should().Contain(SeparatorLabel);
    }

    [TestMethod]
    public void ActionZone_IsHidden_ForTheSystemContext()
    {
        var (cut, _, _, _) = Render(selected: Guid.Empty);

        HasActionZone(cut).Should().BeFalse("the system / no-tenant context cannot be a portal default");
        LabelsOf(cut).Should().NotContain(SeparatorLabel);
    }

    [TestMethod]
    public void ActionZone_IsShown_ForARealTenant_WithNoRecordedPreference()
    {
        var (cut, _, _, _) = Render(selected: Beta, preference: null);

        HasActionZone(cut).Should().BeTrue("a real tenant that is not the recorded preference can be pinned");
        LabelsOf(cut).Should().Contain(SeparatorLabel);
    }

    [TestMethod]
    public void ActionZone_IsHidden_WhenTheSelectedTenantIsAlreadyThePreference()
    {
        var (cut, _, _, _) = Render(selected: Beta, preference: Beta);

        HasActionZone(cut).Should().BeFalse("setting the same tenant again is a no-op");
        LabelsOf(cut).Should().NotContain(SeparatorLabel, "a hidden action zone leaves no dangling separator");
    }

    /// <summary>
    /// <see cref="Guid.Empty"/> is a REAL preference value (the system context), not "no
    /// preference" — every real tenant differs from it, so the action must still be offered.
    /// </summary>
    [TestMethod]
    public void ActionZone_IsShown_WhenThePreferenceIsTheSystemContext()
    {
        var (cut, _, _, _) = Render(selected: Beta, preference: Guid.Empty);

        HasActionZone(cut).Should().BeTrue("a real tenant differs from the system-context preference");
    }

    [TestMethod]
    public void ActionZone_SeparatorIsDisabled_AndTheActionEntryIsNot()
    {
        var (cut, _, _, _) = Render(selected: Beta);

        OptionOf(cut, SeparatorLabel).HasAttribute("disabled").Should().BeTrue(
            "the separator is decorative — native selects have no divider primitive, so it must be unselectable");
        OptionOf(cut, ActionLabel).HasAttribute("disabled").Should().BeFalse(
            "a disabled option cannot be clicked, so the action entry has to stay selectable; the "
            + "'never selected' invariant is enforced by interception instead");
    }

    // ── FR-4 / FR-5 / FR-6: the confirm flow ─────────────────────────────────

    /// <summary>
    /// The confirmed path: the prompt names the tenant and carries the sanctioned wording, the
    /// preference is written, the applied default is re-published, and the circuit reloads so
    /// every tenant-scoped page picks the new default up.
    /// </summary>
    [TestMethod]
    public async Task SetAsDefault_Confirmed_WritesThePreference_PublishesIt_AndReloads()
    {
        var (cut, _, selection, handler) = Render(selected: Beta, preference: null, confirmDialog: true);

        await cut.InvokeAsync(() => OptionOf(cut, ActionLabel).Click());

        var put = PutCalls(handler).Single();
        put.Body.Should().Contain(
            Beta.ToString(), "the operator pins the tenant the dropdown was showing, not the platform default");
        Nav.ForcedReloads.Should().ContainSingle(
            "the forced reload is what makes the new default take effect in this circuit");

        // The re-publish that follows the write is deliberately a no-op HERE: the action zone only
        // renders for a real-tenant pick, and AdminDefaultTenantResolver.ResolveAndApplyAsync
        // short-circuits while an explicit pick is in force (it must not blur the selection and
        // applied-default slots). Asserting the stub's applied slot would therefore pin a
        // no-op; the write + reload above are the observable contract.
    }

    /// <summary>
    /// Cancel writes nothing and leaves the selection untouched (FR-4/FR-6) — driven through the
    /// REAL dialog provider, so the assertion covers the dialog actually being dismissible, not
    /// just a mocked outcome.
    /// </summary>
    [TestMethod]
    public async Task SetAsDefault_Cancelled_WritesNothing_AndKeepsTheSelection()
    {
        var (cut, dialogs, selection, handler) = Render(selected: Beta, preference: null);
        dialogs.Should().NotBeNull();

        // The click handler awaits the dialog result, so dispatch it WITHOUT awaiting the handler
        // chain: awaiting here would never return while the dialog is still open.
        await cut.InvokeAsync(() =>
        {
            // bUnit registers Blazor's @onclick handler under "onclick". The dispatch is
            // fire-and-forget on purpose (see below).
            _ = OptionOf(cut, ActionLabel).TriggerEventAsync("onclick", new MouseEventArgs());
        });

        dialogs!.WaitForAssertion(() => dialogs.Markup.Should().Contain(
            "Set default tenant", "the prompt is the shared modal confirm dialog"));

        // FR-6: while the dialog is open the bound selection is still the tenant, never the action entry.
        SelectOf(cut).Instance.SelectedOption!.Value.Should().Be(Beta.ToString());

        dialogs.Find(".confirm-dialog fluent-button[appearance='outline']").Click();

        cut.WaitForAssertion(() => PutCalls(handler).Should().BeEmpty(
            "cancelling must not record a preference"));
        SelectOf(cut).Instance.SelectedOption!.Value.Should().Be(
            Beta.ToString(), "the dropdown comes back showing the tenant the operator had selected");
        selection.Applied.Should().NotBe(Beta, "nothing was re-published for a cancelled write");
    }

    /// <summary>
    /// A failed write is non-fatal: the dropdown keeps its state and the circuit is not reloaded.
    /// </summary>
    [TestMethod]
    public async Task SetAsDefault_ApiFailure_KeepsTheSelection_AndDoesNotReload()
    {
        var (cut, _, selection, handler) = Render(
            selected: Beta, preference: null, putStatus: HttpStatusCode.InternalServerError, confirmDialog: true);

        await cut.InvokeAsync(() => OptionOf(cut, ActionLabel).Click());

        PutCalls(handler).Should().HaveCount(1, "the attempt is made");
        Nav.ForcedReloads.Should().BeEmpty("a failed write must not reload the circuit");
        SelectOf(cut).Instance.SelectedOption!.Value.Should().Be(Beta.ToString());
        selection.Applied.Should().NotBe(Beta);
    }

    /// <summary>
    /// The forced reload supersedes a toast (the removed button showed none either), and a
    /// success toast would be misleading when the write is followed by a full page load.
    /// </summary>
    [TestMethod]
    public async Task SetAsDefault_DoesNotShowASuccessToast()
    {
        var toastMock = new Mock<IToastService>();
        Services.AddSingleton(toastMock.Object);

        var (cut, _, _, _) = Render(selected: Beta, preference: null, confirmDialog: true);

        await cut.InvokeAsync(() => OptionOf(cut, ActionLabel).Click());

        toastMock.VerifyNoOtherCalls();
    }

    // ── the circuit reconciliation reload ────────────────────────────────────

    /// <summary>
    /// The path this round exists to repair: a first render whose circuit claim is empty while the
    /// portal has resolved a default. The switcher has to ask for one hard reload so the claim is
    /// recomputed from the store — the navigation it used to ask for never produced the document
    /// load, so the claim stayed empty until a manual F5.
    /// </summary>
    [TestMethod]
    public void ReconciliationReload_IsRequested_OnAnInitialRender_WhoseClaimIsEmpty()
    {
        var (cut, _, _, _) = Render(selected: null, preference: null, claimTenant: null);

        cut.WaitForAssertion(() => Nav.ForcedReloads.Should().ContainSingle(
            "the resolved default differs from this circuit's empty claim, so the circuit reloads once"));
    }

    /// <summary>
    /// The production timing the first-render-only check missed: the Settings API answers
    /// asynchronously, so the component's first render is the loading state and <c>_effective</c>
    /// is still null when <c>OnAfterRenderAsync(firstRender: true)</c> fires. The reconciliation
    /// must run on the render that can see the resolved default, or the circuit keeps its empty
    /// claim and the active-term toolbar shows no period until a manual refresh.
    /// </summary>
    [TestMethod]
    public void ReconciliationReload_IsRequested_WhenTheDefaultResolvesAfterTheFirstRender()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var (cut, _, _, _) = Render(selected: null, preference: null, claimTenant: null, gate: gate.Task);

        // The load is still in flight, so there is nothing to reconcile yet — and a first-render-only
        // check would have consumed its one opportunity here, before the default existed.
        Nav.ForcedReloads.Should().BeEmpty("the default has not resolved yet");

        gate.SetResult(true);

        cut.WaitForAssertion(() => LabelsOf(cut).Should().Contain("Alpha"), TimeSpan.FromSeconds(5));

        // The reconciliation is requested from OnAfterRenderAsync, which the bUnit wait helper can
        // observe before or after its own render callback. Force one more render cycle so the
        // assertion sees the reload deterministically; _reloadRequested caps it at one either way.
        cut.Render();

        Nav.ForcedReloads.Should().ContainSingle(
            "the reconciliation must run once the async default resolution completes, not only on the first render");
    }

    // ── FR-2 / FR-6: the separator is inert ──────────────────────────────────

    [TestMethod]
    public async Task ClickingTheSeparator_DoesNothing()
    {
        var (cut, _, selection, handler) = Render(selected: Beta, preference: null);

        await cut.InvokeAsync(() => OptionOf(cut, SeparatorLabel).Click());

        PutCalls(handler).Should().BeEmpty("the separator is decorative — it must never write anything");
        Nav.ForcedReloads.Should().BeEmpty();
        SelectOf(cut).Instance.SelectedOption!.Value.Should().Be(Beta.ToString());
        selection.Applied.Should().NotBe(Beta);
    }

    /// <summary>
    /// Pins the exact prompt the operator reads, so a later rewrite of the confirm wording is a
    /// test failure rather than a silent UX change.
    /// </summary>
    [TestMethod]
    public async Task TheConfirmPrompt_NamesTheTenant_WithTheSanctionedWording()
    {
        var captured = new List<ConfirmDialogContent>();

        var preferenceJson = """{"defaultTenantId":null}""";
        var handler = new ScriptedHandler()
            .Map("GET", "/api/tenants", HttpStatusCode.OK, TenantsJson)
            .Map("GET", "/api/tenants/me/default", HttpStatusCode.OK, preferenceJson)
            .Map("PUT", "/api/tenants/me/default", HttpStatusCode.NoContent, string.Empty);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:FEATURE:DisableOIDCAuth"] = "true",
            })
            .Build();

        var selection = new StubSelection { Selected = Beta };
        var client = new TenantsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://settings-api") });

        Services.AddSingleton<IConfiguration>(config);
        Services.AddSingleton(client);
        Services.AddSingleton<IDevTenantSelection>(selection);
        Services.AddSingleton(new AdminDefaultTenantResolver(
            client, selection, NullLogger<AdminDefaultTenantResolver>.Instance));
        Services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider(UserWithTenant(Beta)));
        Services.AddSingleton<VisibleTenantService>();
        RegisterConfirmDialog(confirm: true, captured);

        var cut = Render<DevTenantSwitcher>();

        await cut.InvokeAsync(() => OptionOf(cut, ActionLabel).Click());

        captured.Should().ContainSingle();
        captured[0].Title.Should().Be("Set default tenant");
        captured[0].Message.Should().Be("Open the admin portal on 'Beta' by default from now on?");
        captured[0].PrimaryText.Should().Be("Set as default");
        captured[0].SecondaryText.Should().Be("Cancel");
    }
}
