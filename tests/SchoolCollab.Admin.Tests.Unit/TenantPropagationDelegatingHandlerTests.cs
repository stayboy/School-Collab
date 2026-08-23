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
        public StubSelection(Guid? value) => _value = value;

        // Optional fault injection: when set, GetSelectedTenantIdAsync throws it.
        public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default)
            => Fault is not null ? Task.FromException<Guid?>(Fault) : Task.FromResult(_value);

        public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CaptureHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    private static HttpClient BuildClient(Guid? selected) =>
        new(new TenantPropagationDelegatingHandler(new StubSelection(selected))
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
