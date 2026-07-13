using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Append-only history of a <see cref="Guardian"/>'s name. Retained on guardian
/// soft-delete (spec §4.2). Tenant-scoped so it passes the tenant-filter audit
/// (spec §5: every new table carries <c>tenant_id</c>).
/// </summary>
public sealed class GuardianNameHistory : ITenantEntity, IEntity, IAuditableEntity
{
    private GuardianNameHistory() { }

    public Guid Id { get; private set; }
    public Guid GuardianId { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public string? DisplayName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static GuardianNameHistory Create(
        Guid guardianId, Guid tenantId, string firstName, string lastName, string? displayName)
    {
        var now = DateTimeOffset.UtcNow;
        return new GuardianNameHistory
        {
            Id = Guid.NewGuid(),
            GuardianId = guardianId,
            TenantId = tenantId,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            DisplayName = displayName?.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
