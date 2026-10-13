using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Admin.Tests.Unit;

[TestClass]
public class TenantPropagationDelegatingHandlerTests
{
    private sealed class StubSelection : IDevTenantSelection
    {
        private readonly Guid? _value;
        public Exception? Fault { get; set; }

        /// <summary>The portal-applied default slot. Null = the portal has applied nothing.</summary>
        public Guid? Applied { get; set; }

        public StubSelection(Guid? value) => _value = value;

        // Optional fault injection: when set, GetSelectedTenantIdAsync throws it.
        public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default)
            => Fault is not null ? Task.FromException<Guid?>(Fault) : Task.FromResult(_value);

        public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default) => Task.CompletedTask;

        // Deliberately NOT fault-injectable: the fault tests exercise the SELECTION read, and the
        // handler only reaches this slot when that read reported "no explicit pick".
        public Task<Guid?> GetAppliedDefaultTenantIdAsync(CancellationToken ct = default) => Task.FromResult(Applied);
        public Task SetAppliedDefaultTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
        {
            Applied = tenantId;
            return Task.CompletedTask;
        }
    }

    private sealed class CaptureHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    private static HttpClient BuildClient(Guid? selected, Guid? applied = null) =>
        new(new TenantPropagationDelegatingHandler(new StubSelection(selected) { Applied = applied })
        {
            InnerHandler = new CaptureHandler()
        });

    [TestMethod]
    public async Task SelectedTenant_Adds_x_tenant_id_Header()
    {
        var selected = Guid.NewGuid();
        using var client = BuildClient(selected);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://x/students");
        await client.SendAsync(request, CancellationToken.None);

        request.Headers.Contains("x-tenant-id").Should().BeTrue();
        request.Headers.GetValues("x-tenant-id").Should().ContainSingle().Which.Should().Be(selected.ToString());
    }

    [TestMethod]
    public async Task NoSelectedTenant_Omits_Header()
    {
        using var client = BuildClient(null);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://x/students");
        await client.SendAsync(request, CancellationToken.None);

        request.Headers.Contains("x-tenant-id").Should().BeFalse("no tenant selected -> no header");
    }

    [TestMethod]
    public async Task EmptyTenant_Omits_Header()
    {
        using var client = BuildClient(Guid.Empty);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://x/students");
        await client.SendAsync(request, CancellationToken.None);

        request.Headers.Contains("x-tenant-id").Should().BeFalse("empty tenant -> no header");
    }

    // ── The portal-applied default slot ─────────────────────────────────────

    [TestMethod]
    public async Task NoSelectedTenant_ButAPortalAppliedDefault_StampsThatTenant()
    {
        var applied = Guid.NewGuid();
        using var client = BuildClient(null, applied);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://x/students");
        await client.SendAsync(request, CancellationToken.None);

        request.Headers.GetValues("x-tenant-id").Should().ContainSingle().Which.Should().Be(applied.ToString(),
            "with no explicit pick, the default the portal applied on load is the tenant the API calls must speak for");
    }

    [TestMethod]
    public async Task SelectedTenant_Wins_OverThePortalAppliedDefault()
    {
        var selected = Guid.NewGuid();
        using var client = BuildClient(selected, applied: Guid.NewGuid());

        var request = new HttpRequestMessage(HttpMethod.Get, "http://x/students");
        await client.SendAsync(request, CancellationToken.None);

        request.Headers.GetValues("x-tenant-id").Should().ContainSingle().Which.Should().Be(selected.ToString(),
            "an explicit pick outranks the applied default in both this handler and TestAuthHandler");
    }

    [TestMethod]
    public async Task AppliedDefaultOfTheSystemContext_Omits_Header()
    {
        // The portal resolved to the system / no-tenant context. There is no tenant to stamp, and
        // Guid.Empty must never travel: the receiving TestAuthHandler re-derives it from the
        // shared store itself, so sending it would be a redundant, misleading header.
        using var client = BuildClient(null, Guid.Empty);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://x/students");
        await client.SendAsync(request, CancellationToken.None);

        request.Headers.Contains("x-tenant-id").Should().BeFalse();
    }

    [TestMethod]
    public async Task Explicit_x_tenant_id_Header_Wins_Over_Selection()
    {
        var selected = Guid.NewGuid();
        var explicitTenant = Guid.NewGuid();
        using var client = BuildClient(selected);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://x/students");
        request.Headers.TryAddWithoutValidation("x-tenant-id", explicitTenant.ToString());
        await client.SendAsync(request, CancellationToken.None);

        request.Headers.GetValues("x-tenant-id").Should().ContainSingle()
            .Which.Should().Be(explicitTenant.ToString(),
                "an explicitly set x-tenant-id must win over the dev selection (the deep-link stamp carries the token's validated tenant)");
    }

    // ── Fault isolation (EnrollStudentDialog regression): a cache read failure
    // inside SendAsync (e.g. Redis down) must NOT fail the dialog's API call —
    // the request proceeds without the header and the receiver falls back to
    // its own IDevTenantSelection/default tenant. See
    // docs/plans/2026-08-22-tenant-propagation-enroll-stream-investigation.md.

    [TestMethod]
    public async Task SelectionReadFails_Request_Proceeds_Without_Header()
    {
        // Simulates DevTenantSelection throwing from IDistributedCache.GetAsync
        // (Redis connection failure / timeout) inside SendAsync.
        using var client = new HttpClient(new TenantPropagationDelegatingHandler(
            new StubSelection(null) { Fault = new InvalidOperationException(
                "It was not possible to connect to the redis server(s).") })
        {
            InnerHandler = new CaptureHandler(),
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "http://x/students");
        var act = async () => await client.SendAsync(request, CancellationToken.None);

        await act.Should().NotThrowAsync(
            "a dev-selection cache failure must be swallowed and the request must proceed");

        request.Headers.Contains("x-tenant-id").Should().BeFalse();
    }

    [TestMethod]
    public async Task SelectionReadFails_SubsequentRequests_Recover()
    {
        // After one failing read, later reads that succeed stamp the header again
        // (e.g. Redis blip then recovery mid-session).
        var stub = new StubSelection(Guid.NewGuid());
        using var client = new HttpClient(new TenantPropagationDelegatingHandler(stub)
        {
            InnerHandler = new CaptureHandler(),
        });

        var first = new HttpRequestMessage(HttpMethod.Get, "http://x/students");
        await client.SendAsync(first, CancellationToken.None);
        first.Headers.GetValues("x-tenant-id").Should().HaveCount(1);

        stub.Fault = new TimeoutException("redis blip");

        var second = new HttpRequestMessage(HttpMethod.Get, "http://x/students");
        await client.SendAsync(second, CancellationToken.None);
        second.Headers.Contains("x-tenant-id").Should().BeFalse("failing read -> no header");

        stub.Fault = null;

        var third = new HttpRequestMessage(HttpMethod.Get, "http://x/students");
        await client.SendAsync(third, CancellationToken.None);
        third.Headers.Contains("x-tenant-id").Should().BeTrue("recovered read -> header stamped again");
    }
}
