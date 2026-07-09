namespace SchoolCollab.Core.Tenancy;

/// <summary>
/// Contract for entities whose tenancy is **hybrid**: the row may belong to a
/// specific tenant (<see cref="TenantId"/> is a real <see cref="Guid"/>) or be a
/// shared blueprint visible to all tenants (<see cref="TenantId"/> is
/// <see langword="null"/>). <see cref="Guid.Empty"/> is never a valid value.
/// </summary>
/// <remarks>
/// <para>The only hybrid entity in the solution is <c>CodedValue</c> (Settings),
/// which is reusable across tenancy: CSV-seeded shared rows (<c>tenant_id IS NULL</c>)
/// are overlaid per tenant via <c>TenantCodedValueOverride</c>, while tenant-owned
/// rows (created by the Grade-Level wizard's "create new" under a real tenant)
/// are isolated to that tenant.</para>
/// <para>The hybrid query filter is <c>TenantId == CurrentTenantId OR TenantId == null</c>,
/// installed via
/// <see cref="SchoolCollab.Core.Data.EntityTypeBuilderExtensions.ConfigureTenantOrGlobalQueryFilter{TEntity}"/>.</para>
/// <para>See <c>documents/specs/global-tenant-filter.md</c> §3.2–§3.3.</para>
/// </remarks>
public interface IHybridTenantEntity
{
    /// <summary>
    /// The tenant that owns this row, or <see langword="null"/> for a shared
    /// blueprint row visible to all tenants. Never <see cref="Guid.Empty"/>.
    /// </summary>
    Guid? TenantId { get; set; }
}
