using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using System.Security.Claims;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="VisibleTenantService"/> — the single source of
/// truth for whether the signed-in user has a real (non-default) tenant.
/// </summary>
[TestClass]
public class VisibleTenantServiceTests
{
    private sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
    {
        private ClaimsPrincipal _user = new();
        public void SetUser(ClaimsPrincipal user) => _user = user;
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(_user));
    }

    private static ClaimsPrincipal WithTenant(string? tenantId, string? tenantName = null)
    {
        var claims = new List<Claim>();
        if (tenantId is not null) claims.Add(new Claim("tenant_id", tenantId));
        if (tenantName is not null) claims.Add(new Claim("tenant_name", tenantName));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [TestMethod]
    public async Task IsRealTenant_True_For_NonEmpty_Guid_Claim()
    {
        var provider = new TestAuthenticationStateProvider();
        provider.SetUser(WithTenant(Guid.NewGuid().ToString(), "Hydeson"));
        var service = new VisibleTenantService(provider, NullLogger<VisibleTenantService>.Instance);

        var scope = await service.GetScopeAsync();

        scope.IsRealTenant.Should().BeTrue();
        scope.TenantId.Should().NotBeNull();
        scope.TenantId.Should().NotBe(Guid.Empty);
        scope.TenantName.Should().Be("Hydeson");
    }

    [TestMethod]
    public async Task IsRealTenant_False_For_Empty_Guid_Claim()
    {
        var provider = new TestAuthenticationStateProvider();
        provider.SetUser(WithTenant(Guid.Empty.ToString(), "System"));
        var service = new VisibleTenantService(provider, NullLogger<VisibleTenantService>.Instance);

        var scope = await service.GetScopeAsync();

        scope.IsRealTenant.Should().BeFalse();
        scope.TenantId.Should().BeNull();
    }

    [TestMethod]
    public async Task IsRealTenant_False_For_Missing_Claim()
    {
        var provider = new TestAuthenticationStateProvider();
        provider.SetUser(WithTenant(null));
        var service = new VisibleTenantService(provider, NullLogger<VisibleTenantService>.Instance);

        var scope = await service.GetScopeAsync();

        scope.IsRealTenant.Should().BeFalse();
        scope.TenantId.Should().BeNull();
    }

    [TestMethod]
    public async Task IsRealTenant_False_For_Unparseable_Claim()
    {
        var provider = new TestAuthenticationStateProvider();
        provider.SetUser(WithTenant("not-a-guid"));
        var service = new VisibleTenantService(provider, NullLogger<VisibleTenantService>.Instance);

        var scope = await service.GetScopeAsync();

        scope.IsRealTenant.Should().BeFalse();
        scope.TenantId.Should().BeNull();
    }
}
