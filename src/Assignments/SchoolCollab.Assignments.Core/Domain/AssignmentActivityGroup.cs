using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// Assignment ↔ ActivityGroup many-to-many link (spec activity-group-enrollment.md
/// FR-17, §8.3). Lives in the Assignments context. <see cref="ActivityGroupId"/>
/// is an operational reference into the Students context (no cross-context DB FK),
/// mirroring <c>Assignment.GradeLevelId</c>/<c>SubjectId</c>. Integrity is enforced
/// in code: FR-21 (same tenant), FR-22 (non-archived), and the referential delete
/// guard FR-6 (a group with any live link cannot be hard-deleted).
/// </summary>
public sealed class AssignmentActivityGroup : ITenantEntity, IEntity, IAuditableEntity
{
    private AssignmentActivityGroup() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: strict tenant entity (§8.3), inherits the assignment's tenant.
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    public Guid AssignmentId { get; private set; }
    public Guid ActivityGroupId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AssignmentActivityGroup Create(Guid tenantId, Guid assignmentId, Guid activityGroupId)
    {
        if (assignmentId == Guid.Empty)
            throw new ArgumentException("Assignment id is required.", nameof(assignmentId));
        if (activityGroupId == Guid.Empty)
            throw new ArgumentException("Activity group id is required.", nameof(activityGroupId));

        var now = DateTimeOffset.UtcNow;
        return new AssignmentActivityGroup
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AssignmentId = assignmentId,
            ActivityGroupId = activityGroupId,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
