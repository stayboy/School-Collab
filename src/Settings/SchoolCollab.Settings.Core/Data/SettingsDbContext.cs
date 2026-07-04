using Microsoft.EntityFrameworkCore;
using SchoolCollab.Settings.Core.Data.Configurations;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Identity;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Data;

/// <summary>
/// Unified DbContext for the Settings bounded context. Combines the CodedValues
/// aggregate (domain hierarchies, tenant overrides, attribute definitions, user
/// lookup) with the FeatureFlag aggregate (global flags, tenant overrides, audit
/// trail). Both aggregates share the Settings outbox and the ITenantProvider for
/// query filters. See documents/solution/settings-context-merge-spec.md §6.
/// </summary>
public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options, ITenantProvider tenantProvider)
    : ModuleDbContext(options, tenantProvider)
{
    // ── CodedValues aggregate ──
    public DbSet<CodedValue> CodedValues => Set<CodedValue>();
    public DbSet<TenantCodedValueOverride> TenantCodedValueOverrides => Set<TenantCodedValueOverride>();
    public DbSet<TenantCodedValueAttributeOverride> TenantCodedValueAttributeOverrides => Set<TenantCodedValueAttributeOverride>();
    public DbSet<User> Users => Set<User>();

    // ── FeatureFlag aggregate ──
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<TenantFeatureFlagOverride> TenantFlagOverrides => Set<TenantFeatureFlagOverride>();
    public DbSet<FlagAuditEntry> FlagAuditEntries => Set<FlagAuditEntry>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // CodedValues configurations
        modelBuilder.ApplyConfiguration(new CodedValueConfiguration());
        modelBuilder.ApplyConfiguration(new TenantCodedValueOverrideConfiguration());
        modelBuilder.ApplyConfiguration(new TenantCodedValueAttributeOverrideConfiguration(() => CurrentTenantId));

        // FeatureFlag configurations
        modelBuilder.ApplyConfiguration(new FeatureFlagConfiguration());
        modelBuilder.ApplyConfiguration(new TenantFeatureFlagOverrideConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new FlagAuditEntryConfiguration());

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(OutboxMapping.FlagsFor<SettingsDbContext>()));
    }
}
