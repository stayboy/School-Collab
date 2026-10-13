using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Services;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The admin portal's load-time resolution chain: explicit switcher pick → operator
/// preference → platform designated default → (nothing, so the configured
/// <c>TestAuthHandlerOptions.TenantId</c> stays in force).
/// </summary>
/// <remarks>
/// The chain is the whole feature, so each link gets its own test, plus the two
/// "the slots must not alias" guards: an explicit pick must not be overwritten into the
/// applied-default slot, and a preference for the system context must survive as
/// <see cref="Guid.Empty"/> rather than being mistaken for "no preference".
/// </remarks>
[TestClass]
public class AdminDefaultTenantResolverTests
{
    private static readonly Guid PlatformDefault = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Preference = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Picked = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly string TenantsWithDefault =
        $$"""
        [{"id":"{{PlatformDefault}}","name":"Alpha","type":"School","isDefault":true},
         {"id":"{{Preference}}","name":"Beta","type":"School","isDefault":false}]
        """;

    private static readonly string TenantsWithoutDefault =
        """
        [{"id":"11111111-1111-1111-1111-111111111111","name":"Alpha","type":"School","isDefault":false}]
        """;

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Calls = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[$"{method.ToUpperInvariant()} {url}"] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = $"{request.Method.Method.ToUpperInvariant()} {request.RequestUri!.PathAndQuery}";
            Calls.Add(key);

            if (_responses.TryGetValue(key, out var hit))
            {
                return Task.FromResult(new HttpResponseMessage(hit.Status)
                {
                    Content = new StringContent(hit.Body, Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {key}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubSelection : IDevTenantSelection
    {
        public Guid? Selected { get; set; }
        public Guid? Applied { get; private set; }
        public int AppliedWrites { get; private set; }

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
            AppliedWrites++;
            return Task.CompletedTask;
        }
    }

    private static (AdminDefaultTenantResolver Resolver, StubSelection Selection, ScriptedHandler Handler) Build(
        string preferenceJson,
        string tenantsJson = "",
        Guid? selected = null)
    {
        var handler = new ScriptedHandler()
            .Map("GET", "/api/tenants/me/default", HttpStatusCode.OK, preferenceJson);

        if (tenantsJson.Length > 0)
            handler.Map("GET", "/api/tenants", HttpStatusCode.OK, tenantsJson);

        var client = new TenantsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://settings-api") });
        var selection = new StubSelection { Selected = selected };

        return (
            new AdminDefaultTenantResolver(client, selection, NullLogger<AdminDefaultTenantResolver>.Instance),
            selection,
            handler);
    }

    private static string PreferenceJson(Guid? tenantId) =>
        tenantId is null ? """{"defaultTenantId":null}""" : $$"""{"defaultTenantId":"{{tenantId}}"}""";

    // ── The chain ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ResolveAndApply_WithNoPick_PrefersTheOperatorsPreference()
    {
        var (resolver, selection, _) = Build(
            PreferenceJson(Preference), TenantsWithDefault);

        var effective = await resolver.ResolveAndApplyAsync(PlatformDefault);

        effective.Should().Be(Preference, "the operator's own preference is more specific than the platform default");
        selection.Applied.Should().Be(Preference, "the resolved default is published so every host agrees");
    }

    [TestMethod]
    public async Task ResolveAndApply_WithNoPreference_FallsBackToThePlatformDefault()
    {
        var (resolver, selection, _) = Build(PreferenceJson(null), TenantsWithDefault);

        var effective = await resolver.ResolveAndApplyAsync(PlatformDefault);

        effective.Should().Be(PlatformDefault);
        selection.Applied.Should().Be(PlatformDefault);
    }

    [TestMethod]
    public async Task ResolveAndApply_WithNeither_AppliesNothing()
    {
        var (resolver, selection, handler) = Build(PreferenceJson(null), TenantsWithoutDefault);

        var effective = await resolver.ResolveAndApplyAsync(
            AdminDefaultTenantResolver.PlatformDefaultOf([]));

        effective.Should().BeNull("nothing is designated, so the configured TestAuthHandler default must stay in force");
        selection.Applied.Should().BeNull();
        handler.Calls.Should().Contain("GET /api/tenants/me/default");
        handler.Calls.Should().NotContain("GET /api/tenants",
            "the caller already loaded the registry, so the resolver must not re-read it");
    }

    /// <summary>
    /// The aliasing guard. An explicit pick already wins in every host, so publishing it into the
    /// applied-default slot would be both redundant and destructive: it would make "the operator
    /// chose this" indistinguishable from "nothing chosen", and a later change to the platform
    /// default would never take effect.
    /// </summary>
    [TestMethod]
    public async Task ResolveAndApply_WithAnExplicitPick_DoesNotTouchTheAppliedDefault()
    {
        var (resolver, selection, handler) = Build(
            PreferenceJson(Preference), TenantsWithDefault, selected: Picked);

        var effective = await resolver.ResolveAndApplyAsync(PlatformDefault);

        effective.Should().Be(Picked);
        selection.AppliedWrites.Should().Be(0, "the applied-default slot is for the DEFAULT, never for an explicit pick");
        selection.Applied.Should().BeNull();
        handler.Calls.Should().BeEmpty("an explicit pick needs no API read at all");
    }

    /// <summary>
    /// <see cref="Guid.Empty"/> means "prefer the system / no-tenant context" — a recorded
    /// preference, not the absence of one, and not the platform default.
    /// </summary>
    [TestMethod]
    public async Task ResolveAndApply_WithAPreferenceForTheSystemContext_AppliesGuidEmpty()
    {
        var (resolver, selection, _) = Build(PreferenceJson(Guid.Empty), TenantsWithDefault);

        var effective = await resolver.ResolveAndApplyAsync(PlatformDefault);

        effective.Should().Be(Guid.Empty,
            "preferring the system context must not fall through to the platform default");
        selection.Applied.Should().Be(Guid.Empty);
    }

    [TestMethod]
    public void PlatformDefaultOf_ReturnsTheFlaggedTenant_OrNull()
    {
        var flagged = new[] { new TenantDto(PlatformDefault, "Alpha", "School", IsDefault: true) };
        var none = new[] { new TenantDto(PlatformDefault, "Alpha", "School") };

        AdminDefaultTenantResolver.PlatformDefaultOf(flagged).Should().Be(PlatformDefault);
        AdminDefaultTenantResolver.PlatformDefaultOf(none).Should().BeNull();
        AdminDefaultTenantResolver.PlatformDefaultOf([]).Should().BeNull();
    }

    [TestMethod]
    public async Task GetPreference_ReturnsNullWhenNothingIsRecorded()
    {
        var (resolver, _, _) = Build(PreferenceJson(null));

        (await resolver.GetPreferenceAsync()).Should().BeNull();
    }

    // ── The circuit-reconciliation predicate ────────────────────────────────
    //
    // A Blazor circuit's tenant claim is frozen at circuit start, so a default applied during
    // load is invisible to TenantGate and to the API hosts until the circuit re-enters through a
    // new request. These tests pin BOTH halves of that: when a reload is required, and why it
    // cannot loop.

    [TestMethod]
    public void NeedsReload_WhenTheClaimIsStale_IsTrue()
    {
        AdminDefaultTenantResolver.NeedsReload(
            explicitSelection: null, effectiveDefault: PlatformDefault, claimTenant: Guid.Empty)
            .Should().BeTrue(
                "the default was just applied but this circuit's claim predates it — without a "
                + "reload the portal would render tenant-scoped pages as if nothing had been applied");
    }

    /// <summary>
    /// The terminator. After the reload the claim is recomputed from the store, so it equals the
    /// value the resolver applies again — the predicate must then be false, or the portal would
    /// reload forever.
    /// </summary>
    [TestMethod]
    public void NeedsReload_AfterTheReload_IsFalse()
    {
        AdminDefaultTenantResolver.NeedsReload(
            explicitSelection: null, effectiveDefault: PlatformDefault, claimTenant: PlatformDefault)
            .Should().BeFalse("the condition must be a fixed point, which is what stops the reload loop");
    }

    [TestMethod]
    public void NeedsReload_WithAnExplicitPick_IsFalse()
    {
        AdminDefaultTenantResolver.NeedsReload(
            explicitSelection: Picked, effectiveDefault: PlatformDefault, claimTenant: Guid.Empty)
            .Should().BeFalse("the claim was computed from that pick at circuit start; nothing to reconcile");
    }

    [TestMethod]
    public void NeedsReload_WhenNothingIsDesignatedAndThereIsNoClaim_IsFalse()
    {
        AdminDefaultTenantResolver.NeedsReload(
            explicitSelection: null, effectiveDefault: null, claimTenant: Guid.Empty)
            .Should().BeFalse("a portal with no default and no claim is already settled");
    }

    [TestMethod]
    public void NeedsReload_WhenNothingIsDesignatedButTheClaimHoldsATenant_IsTrue()
    {
        // The mirror case: the platform default was cleared while a circuit held the old value.
        AdminDefaultTenantResolver.NeedsReload(
            explicitSelection: null, effectiveDefault: null, claimTenant: PlatformDefault)
            .Should().BeTrue();
    }

    [TestMethod]
    public void NeedsReload_ForTheSystemContextPreference_IsFalseWhenTheClaimIsAlreadySystem()
    {
        AdminDefaultTenantResolver.NeedsReload(
            explicitSelection: null, effectiveDefault: Guid.Empty, claimTenant: Guid.Empty)
            .Should().BeFalse("preferring the system context is satisfied by a no-tenant claim");
    }
}
