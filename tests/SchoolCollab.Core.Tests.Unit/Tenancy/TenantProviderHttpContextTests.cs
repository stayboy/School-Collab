using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Tests.Unit.Tenancy;

[TestClass]
public class TenantProviderHttpContextTests
{
    private sealed class StubHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static ClaimsPrincipal AuthenticatedUser(Guid tenantId)
    {
        var claims = new List<Claim> { new("tenant_id", tenantId.ToString()) };
        if (tenantId != Guid.Empty)
        {
            claims.Add(new Claim("tenant_name", "North High"));
            claims.Add(new Claim("tenant_type", "School"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [TestMethod]
    public void GetTenantContext_WithNoAsyncLocal_ResolvesFromHttpContextClaim()
    {
        var tenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var accessor = new StubHttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = AuthenticatedUser(tenantId) }
        };

        var provider = new TenantProvider(accessor);

        var result = provider.GetTenantContext();
        result.TenantId.Should().Be(tenantId);
        result.TenantName.Should().Be("North High");
        result.Type.Should().Be(TenantType.School);
    }

    [TestMethod]
    public void GetTenantContext_WithNoAuthenticatedUser_FallsBackToDefault()
    {
        var accessor = new StubHttpContextAccessor { HttpContext = new DefaultHttpContext() };

        var provider = new TenantProvider(accessor);

        provider.GetTenantContext().Should().Be(new TenantContext(Guid.Empty, "System", TenantType.Organization));
    }

    [TestMethod]
    public void GetTenantContext_WithEmptyTenantClaim_FallsBackToDefault()
    {
        var accessor = new StubHttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = AuthenticatedUser(Guid.Empty) }
        };

        var provider = new TenantProvider(accessor);

        provider.GetTenantContext().Should().Be(new TenantContext(Guid.Empty, "System", TenantType.Organization));
    }

    [TestMethod]
    public void GetTenantContext_ExplicitAsyncLocalTakesPrecedenceOverHttpClaim()
    {
        var claimTenant = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var explicitTenant = new TenantContext(
            Guid.Parse("44444444-4444-4444-4444-444444444444"), "Explicit", TenantType.Organization);

        var accessor = new StubHttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = AuthenticatedUser(claimTenant) }
        };
        var provider = new TenantProvider(accessor);
        provider.SetTenant(explicitTenant);

        provider.GetTenantContext().Should().Be(explicitTenant);
    }
}
