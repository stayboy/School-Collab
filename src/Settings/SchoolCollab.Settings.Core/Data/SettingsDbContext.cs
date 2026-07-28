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
    // ── Tenant registry (global — see TenantConfiguration) ──
    public DbSet<Tenant> Tenants => Set<Tenant>();

    // ── CodedValues aggregate ──
    public DbSet<CodedValue> CodedValues => Set<CodedValue>();
    public DbSet<TenantCodedValueOverride> TenantCodedValueOverrides => Set<TenantCodedValueOverride>();
    public DbSet<TenantCodedValueAttributeOverride> TenantCodedValueAttributeOverrides => Set<TenantCodedValueAttributeOverride>();
    public DbSet<User> Users => Set<User>();

    // ── EntityCodeRule aggregate (auto-generated entity codes — spec §3.1) ──
    public DbSet<EntityCodeRule> EntityCodeRules => Set<EntityCodeRule>();
    public DbSet<TenantEntityCodeRuleOverride> TenantEntityCodeRuleOverrides => Set<TenantEntityCodeRuleOverride>();

    // ── FeatureFlag aggregate ──
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<TenantFeatureFlagOverride> TenantFlagOverrides => Set<TenantFeatureFlagOverride>();
    public DbSet<FlagAuditEntry> FlagAuditEntries => Set<FlagAuditEntry>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// Global entities in this context (no "Tenant" query filter). Per
    /// global-tenant-filter.md §3.2:
    /// <list type="bullet">
    /// <item><see cref="Tenant"/> — the tenant registry itself.</item>
    /// <item><see cref="User"/> — Keycloak-sourced identity lookup (cross-tenant).</item>
    /// <item><see cref="FeatureFlag"/>, <see cref="FlagAuditEntry"/> — admin-created
    ///   infrastructure; tenants toggle via <see cref="TenantFeatureFlagOverride"/>, they
    ///   do not create flags (Q-2).</item>
    /// <item><see cref="OutboxMessage"/> — queue table; TenantId is dispatch-routing
    ///   payload (FR-15), not a scope filter.</item>
    /// </list>
    /// <para>All other entities (<see cref="CodedValue"/>, the two override tables,
    /// <see cref="TenantFeatureFlagOverride"/>) carry a "Tenant" filter — hybrid for
    /// <see cref="CodedValue"/>, strict for the rest.</para>
    /// </summary>
    protected override Type[] GlobalEntityAllowList =>
    [
        typeof(Tenant),
        typeof(User),
        typeof(FeatureFlag),
        typeof(FlagAuditEntry),
        typeof(OutboxMessage),
    ];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tenant registry configuration (global — no query filter)
        modelBuilder.ApplyConfiguration(new TenantConfiguration());

        // CodedValues configurations
        modelBuilder.ApplyConfiguration(new CodedValueConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new TenantCodedValueOverrideConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new TenantCodedValueAttributeOverrideConfiguration(() => CurrentTenantId));

        // EntityCodeRule configuration (hybrid tenant, owns segments)
        modelBuilder.ApplyConfiguration(new EntityCodeRuleConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new TenantEntityCodeRuleOverrideConfiguration(() => CurrentTenantId));

        // FeatureFlag configurations
        modelBuilder.ApplyConfiguration(new FeatureFlagConfiguration());
        modelBuilder.ApplyConfiguration(new TenantFeatureFlagOverrideConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new FlagAuditEntryConfiguration());

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(OutboxMapping.FlagsFor<SettingsDbContext>()));

        // FR-18 / AC-17: build-time model audit — every non-allow-listed, non-owned
        // entity MUST have a "Tenant" named query filter. Fails fast at model build
        // if a new entity is added without proper tenancy configuration.
        ValidateTenantFilters(modelBuilder);
    }
}
