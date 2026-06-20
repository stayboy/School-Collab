using System.Reflection;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Tenancy;

/// <summary>
/// Centralised helper that sets <see cref="ITenantEntity.TenantId"/> on any entity
/// implementing <see cref="ITenantEntity"/>. This is the single authorised use of
/// reflection for tenant assignment in the solution.
///
/// Why reflection here is acceptable:
///   - The target is an interface property (<c>ITenantEntity.TenantId</c>), so the
///     contract is explicit and compiler-validated.
///   - One central call-site means no scattered <c>typeof(X).GetProperty(...)</c>
///     per aggregate — every new tenant-scoped entity is handled automatically.
///   - The alternative (a <c>WithTenant</c> method on every aggregate) duplicates the
///     same one-liner across every entity, which is more code to maintain for
///     identical behaviour.
/// </summary>
public static class TenantEntityExtensions
{
    private static readonly PropertyInfo TenantIdProperty =
        typeof(ITenantEntity).GetProperty(nameof(ITenantEntity.TenantId))!;

    /// <summary>
    /// Sets <see cref="ITenantEntity.TenantId"/> on <paramref name="entity"/>
    /// using the current tenant from <paramref name="tenantProvider"/>.
    /// </summary>
    public static T WithTenant<T>(this T entity, ITenantProvider tenantProvider)
        where T : ITenantEntity
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        TenantIdProperty.SetValue(entity, tenantId);
        return entity;
    }

    /// <summary>
    /// Sets <see cref="ITenantEntity.TenantId"/> on <paramref name="entity"/>
    /// to an explicit tenant ID.
    /// </summary>
    public static T WithTenant<T>(this T entity, Guid tenantId)
        where T : ITenantEntity
    {
        TenantIdProperty.SetValue(entity, tenantId);
        return entity;
    }

    /// <summary>
    /// Verifies that <paramref name="entity"/> belongs to the current tenant.
    /// Throws <see cref="TenantAccessException"/> if there is a mismatch.
    /// </summary>
    public static T EnsureTenantAccess<T>(this T entity, ITenantProvider tenantProvider)
        where T : ITenantEntity
    {
        var expectedTenantId = tenantProvider.GetTenantContext().TenantId;
        if (entity.TenantId != expectedTenantId)
        {
            throw new TenantAccessException(expectedTenantId, entity.TenantId, typeof(T).Name, entity.TenantId);
        }
        return entity;
    }
}
