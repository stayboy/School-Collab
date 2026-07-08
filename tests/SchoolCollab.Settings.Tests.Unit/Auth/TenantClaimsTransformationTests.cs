using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Tests.Unit.Auth;

[TestClass]
public class TenantClaimsTransformationTests
{
    [TestMethod]
    public async Task TransformAsync_WithTenantClaims_SetsTenantContext()
    {
        var tenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var provider = new TenantProvider();
        var transformer = new TenantClaimsTransformation(provider, NullLogger<TenantClaimsTransformation>.Instance);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("tenant_name", "North High"),
            new Claim("tenant_type", "School")
        }, "TestScheme"));

        var transformed = await transformer.TransformAsync(principal);

        transformed.Should().BeSameAs(principal);
        provider.GetTenantContext().Should().Be(new TenantContext(tenantId, "North High", TenantType.School));
    }

    [TestMethod]
    public async Task TransformAsync_WithMissingTenantNameAndType_UsesDefaults()
    {
        var tenantId = Guid.NewGuid();
        var provider = new TenantProvider();
        var transformer = new TenantClaimsTransformation(provider, NullLogger<TenantClaimsTransformation>.Instance);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", tenantId.ToString())
        }, "TestScheme"));

        await transformer.TransformAsync(principal);

        provider.GetTenantContext().Should().Be(new TenantContext(tenantId, "Unknown", TenantType.School));
    }

    [TestMethod]
    public async Task TransformAsync_WithInvalidTenantType_DefaultsToSchool()
    {
        var tenantId = Guid.NewGuid();
        var provider = new TenantProvider();
        var transformer = new TenantClaimsTransformation(provider, NullLogger<TenantClaimsTransformation>.Instance);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("tenant_type", "NotATenantType")
        }, "TestScheme"));

        await transformer.TransformAsync(principal);

        provider.GetTenantContext().Should().Be(new TenantContext(tenantId, "Unknown", TenantType.School));
    }

    [TestMethod]
    public async Task TransformAsync_WithInvalidTenantId_DoesNotChangeExistingTenantContext()
    {
        var existingTenant = new TenantContext(Guid.NewGuid(), "Existing Tenant", TenantType.Organization);
        var provider = new TenantProvider();
        provider.SetTenant(existingTenant);
        var transformer = new TenantClaimsTransformation(provider, NullLogger<TenantClaimsTransformation>.Instance);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", "not-a-guid")
        }, "TestScheme"));

        await transformer.TransformAsync(principal);

        provider.GetTenantContext().Should().Be(existingTenant);
    }

    [TestMethod]
    public async Task TransformAsync_WithoutClaimsIdentity_ReturnsPrincipalUnchangedAndDoesNotSetTenant()
    {
        var provider = new TenantProvider();
        var transformer = new TenantClaimsTransformation(provider, NullLogger<TenantClaimsTransformation>.Instance);
        var principal = new ClaimsPrincipal();

        var transformed = await transformer.TransformAsync(principal);

        transformed.Should().BeSameAs(principal);
        provider.GetTenantContext().Should().Be(new TenantContext(Guid.Empty, "System", TenantType.Organization));
    }
}
