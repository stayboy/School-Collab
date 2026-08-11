using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Join row linking a <see cref="TeacherActivityAssignment"/> to a grade it
/// applies to (v4 spec §3.5). 0..n per activity assignment.
/// </summary>
public sealed class TeacherActivityAssignmentGrade : ITenantEntity, IEntity
{
    private TeacherActivityAssignmentGrade() { }

    public Guid Id { get; private set; }
    public Guid TeacherActivityAssignmentId { get; private set; }
    public Guid GradeLevelId { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    public static TeacherActivityAssignmentGrade Create(Guid teacherActivityAssignmentId, Guid gradeLevelId) =>
        new TeacherActivityAssignmentGrade
        {
            Id = Guid.NewGuid(),
            TeacherActivityAssignmentId = teacherActivityAssignmentId,
            GradeLevelId = gradeLevelId
        };
}
