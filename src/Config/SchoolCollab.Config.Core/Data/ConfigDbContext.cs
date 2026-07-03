using Microsoft.EntityFrameworkCore;
using SchoolCollab.Config.Core.Data.Configurations;
using SchoolCollab.Config.Core.Domain;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Config.Core.Data;

public sealed class ConfigDbContext(DbContextOptions<ConfigDbContext> options, ITenantProvider tenantProvider)
    : ModuleDbContext(options, tenantProvider)
{
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<TenantFeatureFlagOverride> TenantFlagOverrides => Set<TenantFeatureFlagOverride>();
    public DbSet<FlagAuditEntry> FlagAuditEntries => Set<FlagAuditEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations explicitly so constructor-injected dependencies (the
        // tenant id accessor) are available to the tenant-aware configuration base.
        // Do not use ApplyConfigurationsFromAssembly — it cannot inject arguments.
        modelBuilder.ApplyConfiguration(new FeatureFlagConfiguration());
        modelBuilder.ApplyConfiguration(new TenantFeatureFlagOverrideConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new FlagAuditEntryConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(OutboxMapping.FlagsFor<ConfigDbContext>()));
    }
}