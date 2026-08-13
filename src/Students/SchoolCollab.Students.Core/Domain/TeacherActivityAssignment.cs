using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// A teacher↔activity-group assignment (v4 spec §3.5). A teacher is assigned to
/// an activity with an optional role, optionally across multiple grades (the
/// grades are carried by <see cref="TeacherActivityAssignmentGrade"/> join rows).
/// Strict tenant entity.
/// </summary>
public sealed class TeacherActivityAssignment : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<TeacherActivityAssignmentGrade> _grades = [];

    private TeacherActivityAssignment() { }

    public Guid Id { get; private set; }
    public Guid TeacherId { get; private set; }
    public Guid ActivityGroupId { get; private set; }

    /// <summary>Optional coded-value role (<c>TCHROLES</c> parent) on the activity.</summary>
    public Guid? RoleCodedValueId { get; private set; }

    /// <summary>Grades this activity assignment applies to (0..n).</summary>
    public IReadOnlyList<TeacherActivityAssignmentGrade> Grades => _grades.AsReadOnly();

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TeacherActivityAssignment Create(
        Guid teacherId,
        Guid activityGroupId,
        Guid? roleCodedValueId = null,
        IEnumerable<Guid>? gradeLevelIds = null)
    {
        var now = DateTimeOffset.UtcNow;
        var assignment = new TeacherActivityAssignment
        {
            Id = Guid.NewGuid(),
            TeacherId = teacherId,
            ActivityGroupId = activityGroupId,
            RoleCodedValueId = roleCodedValueId,
            CreatedAt = now,
            UpdatedAt = now
        };
        if (gradeLevelIds is not null)
            foreach (var gradeLevelId in gradeLevelIds)
                assignment._grades.Add(TeacherActivityAssignmentGrade.Create(assignment.Id, gradeLevelId));
        return assignment;
    }

    /// <summary>Sets or clears the role. Idempotent when unchanged.</summary>
    public void SetRole(Guid? roleCodedValueId)
    {
        if (RoleCodedValueId == roleCodedValueId) return;
        RoleCodedValueId = roleCodedValueId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Replaces the grade set for this assignment.</summary>
    public void SetGrades(IEnumerable<Guid> gradeLevelIds)
    {
        _grades.Clear();
        foreach (var gradeLevelId in gradeLevelIds)
            _grades.Add(TeacherActivityAssignmentGrade.Create(Id, gradeLevelId));
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
