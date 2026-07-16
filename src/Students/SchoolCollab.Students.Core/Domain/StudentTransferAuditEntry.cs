using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Append-only audit log row for a student grade-level transfer. Transfer is the
/// unified promote/demote mechanism: moving a student to a higher grade level is
/// a promotion, to a lower one a demotion. Records who moved the student, from
/// which grade to which grade, for which period, and the operator's reason.
/// Never updated or deleted.
/// </summary>
public sealed class StudentTransferAuditEntry : ITenantEntity, IEntity, IAuditableEntity
{
    private StudentTransferAuditEntry() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid FromGradeLevelId { get; private set; }
    public Guid ToGradeLevelId { get; private set; }
    public Guid PeriodId { get; private set; }
    public string? Reason { get; private set; }
    public string ActorId { get; private set; } = default!;
    public string ActorDisplayName { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }

    public static StudentTransferAuditEntry Create(
        Guid tenantId,
        Guid studentId,
        Guid fromGradeLevelId,
        Guid toGradeLevelId,
        Guid periodId,
        string? reason,
        string actorId,
        string actorDisplayName)
    {
        var now = DateTimeOffset.UtcNow;
        return new StudentTransferAuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StudentId = studentId,
            FromGradeLevelId = fromGradeLevelId,
            ToGradeLevelId = toGradeLevelId,
            PeriodId = periodId,
            Reason = reason,
            ActorId = actorId,
            ActorDisplayName = actorDisplayName,
            OccurredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
