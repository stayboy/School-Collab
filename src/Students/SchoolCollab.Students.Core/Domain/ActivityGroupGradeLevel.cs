using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Rev. 2 grade-eligibility link (spec activity-group-enrollment.md FR-39/40/41):
/// an <see cref="ActivityGroup"/> declares the set of <see cref="GradeLevel"/>s it
/// accepts. An empty set = any grade is eligible (AC-40). Backs the AddMembership
/// grade check (FR-13/40): the student's active grade-for-period must be in the
/// group's eligible set. Strict tenant entity (FR-41).
/// </summary>
public sealed class ActivityGroupGradeLevel : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private ActivityGroupGradeLevel() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: inherits the group's/grade's tenant.
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    // FK → activity_groups.id, ON DELETE CASCADE (removing the group removes its eligible grades).
    public Guid ActivityGroupId { get; private set; }

    // FK → grade_levels.id, ON DELETE CASCADE.
    public Guid GradeLevelId { get; private set; }

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates an eligible-grade link.</summary>
    public static ActivityGroupGradeLevel Create(Guid activityGroupId, Guid gradeLevelId)
    {
        if (activityGroupId == Guid.Empty)
            throw new ArgumentException("Activity group id is required.", nameof(activityGroupId));
        if (gradeLevelId == Guid.Empty)
            throw new ArgumentException("Grade level id is required.", nameof(gradeLevelId));

        var now = DateTimeOffset.UtcNow;
        return new ActivityGroupGradeLevel
        {
            Id = Guid.NewGuid(),
            ActivityGroupId = activityGroupId,
            GradeLevelId = gradeLevelId,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}