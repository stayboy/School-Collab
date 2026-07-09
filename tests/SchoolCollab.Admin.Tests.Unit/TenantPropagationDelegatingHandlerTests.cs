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
        public StubSelection(Guid? value) => _value = value;
        public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default) => Task.FromResult(_value);
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
}
