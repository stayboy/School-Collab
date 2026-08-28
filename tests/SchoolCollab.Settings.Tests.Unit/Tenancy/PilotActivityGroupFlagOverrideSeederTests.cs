using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.MigrationService.Seeding;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Tests.Unit.Tenancy;

/// <summary>
/// Unit tests for <see cref="PilotActivityGroupFlagOverrideSeeder"/> (Phase 6.1):
/// seeds a <c>TenantFeatureFlagOverride</c> turning <c>FEATURE:EnableActivityGroups</c>
/// ON for the pilot tenant only, idempotently, with an audit row. Uses the InMemory
/// provider + real <c>AddTenancy()</c> so <see cref="ITenantContextAccessor"/> is the
/// production <see cref="TenantContextAccessor"/> (mirrors
/// <see cref="MigrationServiceTenancyTests"/>).
/// </summary>
[TestClass]
public class PilotActivityGroupFlagOverrideSeederTests
{
    private const string FlagKey = FeatureFlagKeys.EnableActivityGroups;

    private static (ServiceProvider Provider, SettingsDbContext Db) CreateScope()
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<SettingsDbContext>(opts =>
            opts.UseInMemoryDatabase($"pilot-override-{Guid.NewGuid():N}"));
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<SettingsDbContext>();
        return (provider, db);
    }

    private static FeatureFlag SeedFlag(SettingsDbContext db, bool isEnabled = false)
    {
        var flag = FeatureFlag.Create(
            FeatureFlag.NormalizeKey(FlagKey),
            "Enable activity-group management",
            null,
            isEnabled: isEnabled);
        db.FeatureFlags.Add(flag);
        db.SaveChanges();
        return flag;
    }

    private static Tenant SeedTenant(SettingsDbContext db, string name = PilotActivityGroupFlagOverrideSeeder.PilotTenantName)
    {
        var tenant = Tenant.Create(name, TenantType.School);
        db.Tenants.Add(tenant);
        db.SaveChanges();
        return tenant;
    }

    private static PilotActivityGroupFlagOverrideSeeder CreateSeeder(ServiceProvider provider)
    {
        var db = provider.GetRequiredService<SettingsDbContext>();
        var accessor = provider.GetRequiredService<ITenantContextAccessor>();
        return new PilotActivityGroupFlagOverrideSeeder(db, accessor, NullLogger<PilotActivityGroupFlagOverrideSeeder>.Instance);
    }

    [TestMethod]
    public async Task Seeds_Override_And_Audit_When_Pilot_Tenant_And_Flag_Exist()
    {
        var (provider, db) = CreateScope();
        var flag = SeedFlag(db);
        var tenant = SeedTenant(db);
        var seeder = CreateSeeder(provider);

        var tenantIdsByName = new Dictionary<string, Guid> { [tenant.Name] = tenant.Id };

        await seeder.SeedAsync(tenantIdsByName);

        var overrides = await db.TenantFlagOverrides
            .IgnoreQueryFilters(["Tenant"])
            .Where(o => o.TenantId == tenant.Id && o.FeatureFlagId == flag.Id)
            .ToListAsync();
        overrides.Should().HaveCount(1);
        var row = overrides[0];
        row.TenantId.Should().Be(tenant.Id);
        row.IsEnabled.Should().BeTrue();
        row.Value.Should().BeNull();
        row.EffectiveFrom.Should().BeNull();
        row.EffectiveTo.Should().BeNull();
        row.Reason.Should().NotBeNullOrWhiteSpace();
        row.Reason.Should().Contain("Pilot rollout");

        var audits = await db.FlagAuditEntries
            .Where(a => a.TenantId == tenant.Id && a.FeatureFlagId == flag.Id)
            .ToListAsync();
        audits.Should().HaveCount(1);
        var audit = audits[0];
        audit.ChangeKind.Should().Be(FlagChangeKind.OverrideCreated);
        audit.TenantId.Should().Be(tenant.Id);
        audit.NewIsEnabled.Should().BeTrue();
        audit.PreviousIsEnabled.Should().BeNull();
        audit.ActorId.Should().Be("system:migrator");
        audit.ActorDisplayName.Should().Be("Migration Service");
    }

    [TestMethod]
    public async Task Is_Idempotent_Second_Run_Is_NoOp()
    {
        var (provider, db) = CreateScope();
        var flag = SeedFlag(db);
        var tenant = SeedTenant(db);
        var seeder = CreateSeeder(provider);
        var tenantIdsByName = new Dictionary<string, Guid> { [tenant.Name] = tenant.Id };

        await seeder.SeedAsync(tenantIdsByName);
        await seeder.SeedAsync(tenantIdsByName);

        var overrides = await db.TenantFlagOverrides
            .IgnoreQueryFilters(["Tenant"])
            .Where(o => o.TenantId == tenant.Id && o.FeatureFlagId == flag.Id)
            .ToListAsync();
        overrides.Should().HaveCount(1);

        var audits = await db.FlagAuditEntries
            .Where(a => a.TenantId == tenant.Id && a.FeatureFlagId == flag.Id)
            .ToListAsync();
        audits.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task Skips_When_Pilot_Tenant_Not_In_Dictionary()
    {
        var (provider, db) = CreateScope();
        var flag = SeedFlag(db);
        var seeder = CreateSeeder(provider);

        await seeder.SeedAsync(new Dictionary<string, Guid>());

        var overrides = await db.TenantFlagOverrides
            .IgnoreQueryFilters(["Tenant"])
            .Where(o => o.FeatureFlagId == flag.Id)
            .ToListAsync();
        overrides.Should().BeEmpty();

        var audits = await db.FlagAuditEntries
            .Where(a => a.FeatureFlagId == flag.Id)
            .ToListAsync();
        audits.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Skips_When_Flag_Not_Seeded_Yet()
    {
        var (provider, db) = CreateScope();
        var tenant = SeedTenant(db);
        var seeder = CreateSeeder(provider);
        var tenantIdsByName = new Dictionary<string, Guid> { [tenant.Name] = tenant.Id };

        await seeder.SeedAsync(tenantIdsByName);

        var overrides = await db.TenantFlagOverrides
            .IgnoreQueryFilters(["Tenant"])
            .Where(o => o.TenantId == tenant.Id)
            .ToListAsync();
        overrides.Should().BeEmpty();

        var audits = await db.FlagAuditEntries
            .Where(a => a.TenantId == tenant.Id)
            .ToListAsync();
        audits.Should().BeEmpty();
    }
}
