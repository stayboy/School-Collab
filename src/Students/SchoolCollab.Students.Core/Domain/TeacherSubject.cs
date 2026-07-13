using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Link between a teacher and a subject they teach (spec §4.12).
/// </summary>
public sealed class TeacherSubject : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private TeacherSubject() { }

    public Guid Id { get; private set; }
    public Guid TeacherId { get; private set; }
    public Guid SubjectId { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TeacherSubject Create(Guid teacherId, Guid subjectId) =>
        new TeacherSubject
        {
            Id = Guid.NewGuid(),
            TeacherId = teacherId,
            SubjectId = subjectId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
}
