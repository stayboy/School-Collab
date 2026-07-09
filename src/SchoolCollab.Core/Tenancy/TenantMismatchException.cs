namespace SchoolCollab.Core.Tenancy;

/// <summary>
/// Thrown by <see cref="SchoolCollab.Core.Data.ModuleDbContext"/>'s save-guard
/// when an entity being saved carries a <c>TenantId</c> that differs from the
/// current tenant context, and the tenant guard is not suppressed.
/// </summary>
/// <remarks>
/// This catches cross-tenant writes that would otherwise occur if a developer
/// bypassed the query filter to read another tenant's row and then modified it.
/// See <c>global-tenant-filter.md</c> FR-6 / FR-8.
/// </remarks>
public sealed class TenantMismatchException : InvalidOperationException
{
    /// <summary>The tenant id that was expected (the current context).</summary>
    public Guid ExpectedTenantId { get; }

    /// <summary>The tenant id found on the entity being saved.</summary>
    public Guid ActualTenantId { get; }

    /// <summary>The entity type whose tenant did not match.</summary>
    public Type EntityType { get; }

    public TenantMismatchException(Guid expectedTenantId, Guid actualTenantId, Type entityType)
        : base(
            $"Tenant mismatch saving {entityType.Name}: the entity belongs to tenant "
            + $"{actualTenantId} but the current context is tenant {expectedTenantId}. "
            + "Cross-tenant writes are not permitted. Wrap in ITenantContextAccessor "
            + "RunWithExplicitTenantAsync if this is an intentional admin/cross-tenant operation.")
    {
        ExpectedTenantId = expectedTenantId;
        ActualTenantId = actualTenantId;
        EntityType = entityType;
    }
}
