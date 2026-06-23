using Microsoft.EntityFrameworkCore;
using SchoolCollab.CodedValues.Core.Data.Configurations;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Messaging;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Identity;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.CodedValues.Core.Data;

public sealed class CodedValuesDbContext(DbContextOptions<CodedValuesDbContext> options, ITenantProvider tenantProvider)
    : ModuleDbContext(options, tenantProvider)
{
    public DbSet<CodedValue> CodedValues => Set<CodedValue>();
    public DbSet<TenantCodedValueOverride> TenantCodedValueOverrides => Set<TenantCodedValueOverride>();
    public DbSet<TenantCodedValueAttributeOverride> TenantCodedValueAttributeOverrides => Set<TenantCodedValueAttributeOverride>();
    public DbSet<User> Users => Set<User>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations explicitly so constructor-injected dependencies are
        // available to tenant-aware configuration base classes. Do not use
        // ApplyConfigurationsFromAssembly here because it cannot inject arguments.
        modelBuilder.ApplyConfiguration(new CodedValueConfiguration());
        modelBuilder.ApplyConfiguration(new TenantCodedValueOverrideConfiguration());
        modelBuilder.ApplyConfiguration(new TenantCodedValueAttributeOverrideConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
