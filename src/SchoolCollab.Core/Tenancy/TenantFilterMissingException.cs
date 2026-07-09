namespace SchoolCollab.Core.Tenancy;

/// <summary>
/// Thrown by the build-time model audit (<c>ModuleDbContext.ValidateTenantFilters</c>)
/// when a non-allow-listed entity type in a module's EF Core model lacks a
/// configured <c>"Tenant"</c> named query filter.
/// </summary>
/// <remarks>
/// This is the last line of defence against a developer forgetting to configure
/// a tenant filter on a new entity. The audit runs during <c>OnModelCreating</c>
/// so the exception surfaces at first model use (startup / first query), not in
/// production. See <c>global-tenant-filter.md</c> FR-14 / AC-17.
/// </remarks>
public sealed class TenantFilterMissingException : InvalidOperationException
{
    /// <summary>The entity type that lacks a "Tenant" query filter.</summary>
    public Type EntityType { get; }

    /// <summary>The <see cref="DbContext"/> that owns the model being audited.</summary>
    public string DbContextName { get; }

    public TenantFilterMissingException(Type entityType, string dbContextName)
        : base(
            $"Entity type '{entityType.FullName}' in {dbContextName} has no 'Tenant' "
            + "query filter and is not on the context's global-entity allow-list. "
            + "Every tenant-scoped entity MUST be configured with a strict or hybrid "
            + "tenant filter. If the entity is intentionally global (e.g. Tenant, "
            + "FeatureFlag, OutboxMessage), add it to the context's GlobalEntityAllowList.")
    {
        EntityType = entityType;
        DbContextName = dbContextName;
    }
}
