using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// A real tenant (school, organization, or team) registered in the system.
/// This is the source of truth for tenant identity: <see cref="TenantCodedValueOverride"/>
/// and <see cref="TenantFeatureFlagOverride"/> reference <see cref="Id"/>, and the
/// OIDC <c>tenant_id</c> claim is expected to resolve to a row here.
/// </summary>
/// <remarks>
/// Global by design: a tenant row is not itself tenant-scoped, so this entity does
/// <b>not</b> implement <see cref="ITenantEntity"/> and therefore gets no global
/// query filter. It is globally queryable so the dev tenant switcher and the
/// migration seeder can list every tenant without a tenant context.
/// Seeding is idempotent by the natural key <see cref="Name"/> (see
/// <c>TenantSeeder</c>), mirroring the <c>CodedValueSeeder</c> pattern. Tenant ids
/// follow the project convention <c>Id = Guid.NewGuid()</c> via <see cref="Create"/>:
/// no hardcoded Guids are ever seeded.
/// </remarks>
public sealed class Tenant : IEntity, IAuditableEntity
{
    private Tenant() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public TenantType Type { get; private set; }

    /// <summary>
    /// Whether this tenant is the platform's designated default tenant for the
    /// admin portal — the tenant a portal load falls back to when the operator has
    /// expressed no preference of their own.
    /// </summary>
    /// <remarks>
    /// Deliberately a flag on the tenant row rather than a separate settings table:
    /// "designate a default" is a property of one tenant, and a partial unique index
    /// (<c>ix_tenants_is_default_unique</c>) keeps the invariant "at most one default"
    /// in the database, not in application code.
    /// </remarks>
    public bool IsDefault { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a new tenant with a fresh <see cref="Id"/>. The caller captures the
    /// returned <see cref="Id"/> to wire related seed data (overrides, sample
    /// gradelevels) in the same seed pass.
    /// </summary>
    /// <param name="isDefault">
    /// When <see langword="true"/>, the row is created already designated as the
    /// platform default. Only the dev seeder passes this; the admin UI designates
    /// through <see cref="MarkAsDefault"/> so the previous default can be cleared in
    /// the same unit of work.
    /// </param>
    public static Tenant Create(string name, TenantType type, bool isDefault = false)
    {
        var trimmed = name?.Trim()
            ?? throw new ArgumentNullException(nameof(name));

        if (trimmed.Length is 0 or > 200)
            throw new ArgumentOutOfRangeException(nameof(name),
                "Tenant name must be 1–200 characters.");

        var now = DateTimeOffset.UtcNow;
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = trimmed,
            Type = type,
            IsDefault = isDefault,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Renames or re-types an existing tenant. Used by the (future) Tenant admin UI.
    /// </summary>
    public void Update(string name, TenantType type)
    {
        var trimmed = name?.Trim()
            ?? throw new ArgumentNullException(nameof(name));

        if (trimmed.Length is 0 or > 200)
            throw new ArgumentOutOfRangeException(nameof(name),
                "Tenant name must be 1–200 characters.");

        Name = trimmed;
        Type = type;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Designates this tenant as the platform default. Idempotent: re-designating the
    /// current default is a no-op, so the caller can apply it unconditionally.
    /// </summary>
    /// <remarks>
    /// Clearing the PREVIOUS default is the caller's job (one unit of work, one
    /// <c>SaveChanges</c>) — the entity cannot see its siblings. See
    /// <c>SetDefaultTenantHandler</c>.
    /// </remarks>
    public void MarkAsDefault()
    {
        if (IsDefault)
            return;

        IsDefault = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Removes this tenant's platform-default designation. Idempotent, mirroring
    /// <see cref="MarkAsDefault"/>.
    /// </summary>
    public void ClearDefault()
    {
        if (!IsDefault)
            return;

        IsDefault = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}