using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Link between a teacher and a grade level they teach (spec §4.12).
/// </summary>
public sealed class TeacherGradeLevel : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private TeacherGradeLevel() { }

    public Guid Id { get; private set; }
    public Guid TeacherId { get; private set; }
    public Guid GradeLevelId { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TeacherGradeLevel Create(Guid teacherId, Guid gradeLevelId) =>
        new TeacherGradeLevel
        {
            Id = Guid.NewGuid(),
            TeacherId = teacherId,
            GradeLevelId = gradeLevelId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
}
