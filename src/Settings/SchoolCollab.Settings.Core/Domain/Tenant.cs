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
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a new tenant with a fresh <see cref="Id"/>. The caller captures the
    /// returned <see cref="Id"/> to wire related seed data (overrides, sample
    /// gradelevels) in the same seed pass.
    /// </summary>
    public static Tenant Create(string name, TenantType type)
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
}