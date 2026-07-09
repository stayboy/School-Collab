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
    /// and <see cref="IAuditableEntity.UpdatedAt"/> before EF Core persists changes, and
    /// enforces the tenant save-guards (FR-5/FR-6/FR-8 in <c>global-tenant-filter.md</c>):
    /// no strict entity may be saved with an empty <c>TenantId</c>, and no entity may be
    /// saved with a <c>TenantId</c> that mismatches the current context (unless the guard
    /// is suppressed via <see cref="ITenantContextAccessor.SuppressTenantGuard"/>).
    /// </summary>
    private void PrepareChanges()
    {
        var now = DateTimeOffset.UtcNow;
        var currentTenantId = CurrentTenantId;
        var guardSuppressed = TenantContextAccessor.IsGuardSuppressed;

        foreach (var entry in ChangeTracker.Entries())
        {
            // ── Strict tenant entities (ITenantEntity): NOT NULL tenant_id ──
            if (entry.Entity is ITenantEntity tenantEntity)
            {
                if (entry.State == EntityState.Added && tenantEntity.TenantId == Guid.Empty)
                {
                    // Preserve existing convenience: default an unset tenant to the current
                    // context. If the current context is also Guid.Empty, the guard below throws.
                    tenantEntity.TenantId = currentTenantId;
                }

                // Guards apply only to entries that generate SQL (Added/Modified/Deleted).
                // Unchanged/Detached entries are just tracked — e.g. a tenant-A row still in
                // the ChangeTracker when SaveChanges runs under tenant B must NOT trigger a
                // mismatch (it is not being written).
                if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                {
                    // FR-5: no strict entity may be persisted with Guid.Empty (unless suppressed).
                    if (!guardSuppressed && tenantEntity.TenantId == Guid.Empty)
                    {
                        throw new TenantContextRequiredException("SaveChanges", entry.Entity.GetType());
                    }

                    // FR-6: mismatch guard — catches cross-tenant writes.
                    if (!guardSuppressed
                        && tenantEntity.TenantId != Guid.Empty
                        && tenantEntity.TenantId != currentTenantId)
                    {
                        throw new TenantMismatchException(
                            currentTenantId, tenantEntity.TenantId, entry.Entity.GetType());
                    }
                }
            }

            // ── Hybrid tenant entities (IHybridTenantEntity): nullable tenant_id ──
            if (entry.Entity is IHybridTenantEntity hybridEntity)
            {
                if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                {
                    // FR-8: Guid.Empty is never valid for hybrid (null = blueprint is OK).
                    if (!guardSuppressed && hybridEntity.TenantId == Guid.Empty)
                    {
                        throw new TenantContextRequiredException("SaveChanges", entry.Entity.GetType());
                    }

                    // FR-8: mismatch guard for non-null, non-empty hybrid rows.
                    if (!guardSuppressed
                        && hybridEntity.TenantId is { } tid
                        && tid != Guid.Empty
                        && tid != currentTenantId)
                    {
                        throw new TenantMismatchException(currentTenantId, tid, entry.Entity.GetType());
                    }
                }
            }

            // ── Audit timestamps (existing behaviour) ──
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

    /// <summary>
    /// Entity types in this context that are intentionally global (no tenant filter).
    /// Override in derived contexts to list the context's global entities
    /// (e.g. <c>Tenant</c>, <c>FeatureFlag</c>, <c>OutboxMessage</c>). Entities not
    /// on this list MUST have a <c>"Tenant"</c> query filter configured. See FR-14.
    /// </summary>
    protected virtual Type[] GlobalEntityAllowList => [];

    /// <summary>
    /// Build-time model audit (FR-14 / AC-17): enumerates every entity type in the
    /// model and throws <see cref="TenantFilterMissingException"/> if a
    /// non-allow-listed, non-owned entity lacks a <c>"Tenant"</c> named query filter.
    /// Call at the end of <see cref="OnModelCreating"/> in derived contexts.
    /// </summary>
    protected void ValidateTenantFilters(ModelBuilder modelBuilder)
    {
        var allowList = GlobalEntityAllowList;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Owned types inherit the owner's filter via Include — no own filter needed.
            if (entityType.IsOwned())
            {
                continue;
            }

            if (allowList.Contains(entityType.ClrType))
            {
                continue;
            }

            if (entityType.FindDeclaredQueryFilter("Tenant") is null)
            {
                throw new TenantFilterMissingException(entityType.ClrType, GetType().Name);
            }
        }
    }
}
