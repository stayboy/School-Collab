using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Config.Core.CQRS.FeatureFlags.Queries;
using SchoolCollab.Config.Core.Data;
using SchoolCollab.Config.Core.Domain;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Config.Tests.Unit;

[TestClass]
public class ResolveFlagsForTenantHandlerTests : IDisposable
{
    private ConfigDbContext NewDb(ITenantProvider provider)
    {
        var options = new DbContextOptionsBuilder<ConfigDbContext>()
            .UseInMemoryDatabase($"ResolveTest_{Guid.NewGuid()}")
            .Options;
        return new ConfigDbContext(options, provider);
    }

    [TestMethod]
    public async Task Returns_global_default_when_no_override()
    {
        var db = NewDb(new DesignTimeTenantProvider());
        db.FeatureFlags.Add(FeatureFlag.Create("FEATURE:EnableCodedValuesAiChat", "AI chat", null, true));
        await db.SaveChangesAsync();

        var result = await new ResolveFlagsForTenantHandler(db).HandleAsync(new ResolveFlagsForTenant(null));

        result.Should().ContainSingle();
        result[0].IsEnabled.Should().BeTrue();
        result[0].Source.Should().Be("GlobalDefault");
    }

    [TestMethod]
    public async Task Tenant_override_pinned_value_wins_over_global_default()
    {
        var tenant = Guid.NewGuid();
        // A context whose CurrentTenantId matches the override so the tenant query
        // filter lets the row through on read.
        var db = NewDb(new FixedTenantProvider(tenant));
        var flag = FeatureFlag.Create("FEATURE:EnableCodedValuesAiChat", "AI chat", null, isEnabled: true);
        db.FeatureFlags.Add(flag);
        db.TenantFlagOverrides.Add(
            TenantFeatureFlagOverride.Create(tenant, flag.Id, isEnabled: false, "no entitlement", null, null));
        await db.SaveChangesAsync();

        var result = await new ResolveFlagsForTenantHandler(db).HandleAsync(new ResolveFlagsForTenant(tenant));

        result.Should().ContainSingle();
        result[0].IsEnabled.Should().BeFalse();
        result[0].Source.Should().Be("TenantOverride");
    }

    [TestMethod]
    public async Task Null_override_inherits_global_default()
    {
        var tenant = Guid.NewGuid();
        var db = NewDb(new FixedTenantProvider(tenant));
        var flag = FeatureFlag.Create("FEATURE:X", "X", null, isEnabled: true);
        db.FeatureFlags.Add(flag);
        db.TenantFlagOverrides.Add(
            TenantFeatureFlagOverride.Create(tenant, flag.Id, isEnabled: null, "explicit inherit", null, null));
        await db.SaveChangesAsync();

        var result = await new ResolveFlagsForTenantHandler(db).HandleAsync(new ResolveFlagsForTenant(tenant));

        result.Should().ContainSingle();
        result[0].IsEnabled.Should().BeTrue();   // null override → inherit global true
        result[0].Source.Should().Be("GlobalDefault");
    }

    [TestMethod]
    public async Task Archived_flags_are_excluded_from_resolution()
    {
        var db = NewDb(new DesignTimeTenantProvider());
        var flag = FeatureFlag.Create("FEATURE:Old", "Old", null, true);
        flag.Archive();
        db.FeatureFlags.Add(flag);
        await db.SaveChangesAsync();

        var result = await new ResolveFlagsForTenantHandler(db).HandleAsync(new ResolveFlagsForTenant(null));
        result.Should().BeEmpty();
    }

    public void Dispose() { }

    private sealed class FixedTenantProvider(Guid tenantId) : ITenantProvider
    {
        public TenantContext GetTenantContext() => new(tenantId, "Test", TenantType.Organization);
    }
}