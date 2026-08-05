using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Link between a teacher and a qualification / specialty coded value
/// (<c>QUALIF</c> parent). A teacher may hold multiple qualifications
/// (grade-detail-rich-grids-plan.md §3). Coded values live in the Settings
/// database, so <see cref="CodedValueId"/> is a bare tenant-scoped id (no FK),
/// mirroring <see cref="TeacherGradeLevel.TeacherRoleCodedValueId"/>.
/// </summary>
public sealed class TeacherQualification : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private TeacherQualification() { }

    public Guid Id { get; private set; }
    public Guid TeacherId { get; private set; }
    public Guid CodedValueId { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TeacherQualification Create(Guid teacherId, Guid codedValueId) =>
        new TeacherQualification
        {
            Id = Guid.NewGuid(),
            TeacherId = teacherId,
            CodedValueId = codedValueId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
}
