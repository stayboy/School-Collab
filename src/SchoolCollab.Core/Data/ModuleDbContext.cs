using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Data;

/// <summary>
/// Base <see cref="DbContext"/> for bounded-context modules that require multi-tenancy
/// and automatic audit timestamp management.
/// </summary>
public abstract class ModuleDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    protected ModuleDbContext(DbContextOptions options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    /// <summary>
    /// The current tenant id for this context instance. Referenced by global query filters
    /// configured in <see cref="IEntityTypeConfiguration{TEntity}"/> base classes so EF Core
    /// can parameterize the tenant predicate per query while caching the model once.
    /// </summary>
    public Guid CurrentTenantId => _tenantProvider.GetTenantContext().TenantId;

    public override int SaveChanges()
    {
        PrepareChanges();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Stamps <see cref="ITenantEntity.TenantId"/>, <see cref="IAuditableEntity.CreatedAt"/>
    /// and <see cref="IAuditableEntity.UpdatedAt"/> before EF Core persists changes.
    /// </summary>
    private void PrepareChanges()
    {
        var now = DateTimeOffset.UtcNow;
        var currentTenantId = CurrentTenantId;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantEntity tenantEntity && entry.State == EntityState.Added)
            {
                // Allow explicit cross-tenant overrides; only default when not already set.
                if (tenantEntity.TenantId == Guid.Empty)
                {
                    tenantEntity.TenantId = currentTenantId;
                }
            }

            if (entry.Entity is IAuditableEntity &&
                entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property(nameof(IAuditableEntity.UpdatedAt)).CurrentValue = now;

                if (entry.State == EntityState.Added)
                {
                    var createdAtProperty = entry.Property(nameof(IAuditableEntity.CreatedAt));
                    if (createdAtProperty.CurrentValue is DateTimeOffset { Ticks: 0 })
                    {
                        createdAtProperty.CurrentValue = now;
                    }
                }
                else
                {
                    // Never overwrite the creation timestamp on update.
                    entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
                }
            }
        }
    }
}
