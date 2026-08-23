using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Core.Tests.Unit.Auth;

/// <summary>
/// Unit tests for <see cref="TenantForwardingDelegatingHandler"/> — the API→API
/// counterpart to <see cref="TenantPropagationDelegatingHandler"/>. Forwards the
/// CURRENT inbound request's resolved tenant onto outgoing service-to-service
/// calls: inbound x-tenant-id header first, tenant_id claim second, nothing if
/// neither is present (background/dispatch callers stay unscoped).
/// </summary>
[TestClass]
public class TenantForwardingDelegatingHandlerTests
{
    private sealed class CaptureHandler : DelegatingHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (HttpClient Client, CaptureHandler Capture) BuildClient(IHttpContextAccessor accessor)
    {
        var capture = new CaptureHandler();
        var client = new HttpClient(new TenantForwardingDelegatingHandler(accessor)
        {
            InnerHandler = capture,
        });
        return (client, capture);
    }

    private static IHttpContextAccessor AccessorFor(Action<DefaultHttpContext> configure)
    {
        var context = new DefaultHttpContext();
        configure(context);
        return new HttpContextAccessor { HttpContext = context };
    }

    [TestMethod]
    public async Task Inbound_x_tenant_id_Header_Is_Forwarded()
    {
        var tenantId = Guid.NewGuid();
        var (client, capture) = BuildClient(AccessorFor(ctx =>
            ctx.Request.Headers["x-tenant-id"] = tenantId.ToString()));

        await client.GetAsync("http://settings-api/api/coded-values/x");

        capture.LastRequest!.Headers.Contains("x-tenant-id").Should().BeTrue();
        capture.LastRequest.Headers.GetValues("x-tenant-id").Should()
            .ContainSingle().Which.Should().Be(tenantId.ToString());
    }

    [TestMethod]
    public async Task Tenant_id_Claim_Is_Used_When_Header_Absent()
    {
        var tenantId = Guid.NewGuid();
        var (client, capture) = BuildClient(AccessorFor(ctx =>
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("tenant_id", tenantId.ToString()),
            }, "test"));
        }));

        await client.GetAsync("http://settings-api/api/coded-values/x");

        capture.LastRequest!.Headers.GetValues("x-tenant-id").Should()
            .ContainSingle().Which.Should().Be(tenantId.ToString());
    }

    [TestMethod]
    public async Task Header_Takes_Precedence_Over_Claim()
    {
        var headerTenant = Guid.NewGuid();
        var claimTenant = Guid.NewGuid();
        var (client, capture) = BuildClient(AccessorFor(ctx =>
        {
            ctx.Request.Headers["x-tenant-id"] = headerTenant.ToString();
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("tenant_id", claimTenant.ToString()),
            }, "test"));
        }));

        await client.GetAsync("http://settings-api/api/coded-values/x");

        capture.LastRequest!.Headers.GetValues("x-tenant-id").Should()
            .ContainSingle().Which.Should().Be(headerTenant.ToString(),
                "the inbound header is exactly what TestAuthHandler consumed — forwarding it reproduces the receiver's resolution");
    }

    [TestMethod]
    public async Task No_HttpContext_No_Header_Stamped()
    {
        var (client, capture) = BuildClient(new HttpContextAccessor { HttpContext = null });

        await client.GetAsync("http://settings-api/api/coded-values/x");

        capture.LastRequest!.Headers.Contains("x-tenant-id").Should().BeFalse(
            "background/dispatch callers without a request context must stay unscoped");
    }

    [TestMethod]
    public async Task Empty_Tenant_And_No_Claim_No_Header_Stamped()
    {
        var (client, capture) = BuildClient(AccessorFor(ctx =>
            ctx.Request.Headers["x-tenant-id"] = Guid.Empty.ToString()));

        await client.GetAsync("http://settings-api/api/coded-values/x");

        capture.LastRequest!.Headers.Contains("x-tenant-id").Should().BeFalse(
            "an empty/default tenant must not be propagated");
    }
}
