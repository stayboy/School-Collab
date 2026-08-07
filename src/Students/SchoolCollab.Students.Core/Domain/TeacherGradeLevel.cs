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

    /// <summary>
    /// Optional coded-value FK (<c>TCHROLES</c> parent) naming the role this
    /// teacher holds on this grade (e.g. Head of Grade, Class Teacher). Nullable
    /// so existing links and untagged teachers carry null. Tenant-definable.
    /// Mirrors <see cref="StudentGuardian.RelationshipCodedValueId"/>.
    /// </summary>
    public Guid? TeacherRoleCodedValueId { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TeacherGradeLevel Create(Guid teacherId, Guid gradeLevelId, Guid? teacherRoleCodedValueId = null) =>
        new TeacherGradeLevel
        {
            Id = Guid.NewGuid(),
            TeacherId = teacherId,
            GradeLevelId = gradeLevelId,
            TeacherRoleCodedValueId = teacherRoleCodedValueId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    /// <summary>
    /// Sets or clears the role this teacher holds on this grade. Idempotent when
    /// the value is unchanged; otherwise stamps <see cref="UpdatedAt"/>.
    /// </summary>
    public void SetRole(Guid? teacherRoleCodedValueId)
    {
        if (TeacherRoleCodedValueId == teacherRoleCodedValueId) return;
        TeacherRoleCodedValueId = teacherRoleCodedValueId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
