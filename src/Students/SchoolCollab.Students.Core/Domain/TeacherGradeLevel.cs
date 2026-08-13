using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Link between a teacher and a grade level they teach (spec §4.12). Carries an
/// optional <see cref="TopicId"/> so a row is either a plain <c>grade + role</c>
/// assignment or a <c>grade + subject + role</c> assignment. A teacher may hold
/// multiple rows per grade (one per subject, plus an optional grade-only row).
/// v4 — the standalone <see cref="TeacherTopic"/> subject link is superseded by
/// the grade-scoped <see cref="TopicId"/> on this link.
/// </summary>
public sealed class TeacherGradeLevel : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private TeacherGradeLevel() { }

    public Guid Id { get; private set; }
    public Guid TeacherId { get; private set; }
    public Guid GradeLevelId { get; private set; }

    /// <summary>
    /// Optional subject (topic) the teacher teaches in this grade. <c>null</c> =
    /// a grade-only assignment. A teacher can teach multiple subjects in a grade by
    /// holding one row per subject.
    /// </summary>
    public Guid? TopicId { get; private set; }

    /// <summary>
    /// Optional coded-value FK (<c>TCHROLES</c> parent) naming the role this
    /// teacher holds on this assignment (e.g. Head of Grade, Class Teacher, Subject
    /// Lead). Nullable so existing links and untagged teachers carry null.
    /// Tenant-definable. Mirrors <see cref="StudentGuardian.RelationshipCodedValueId"/>.
    /// </summary>
    public Guid? TeacherRoleCodedValueId { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TeacherGradeLevel Create(Guid teacherId, Guid gradeLevelId, Guid? topicId = null, Guid? teacherRoleCodedValueId = null) =>
        new TeacherGradeLevel
        {
            Id = Guid.NewGuid(),
            TeacherId = teacherId,
            GradeLevelId = gradeLevelId,
            TopicId = topicId,
            TeacherRoleCodedValueId = teacherRoleCodedValueId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    /// <summary>
    /// Sets or clears the subject taught in this grade. Idempotent when unchanged.
    /// </summary>
    public void SetTopic(Guid? topicId)
    {
        if (TopicId == topicId) return;
        TopicId = topicId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Sets or clears the role this teacher holds on this assignment. Idempotent when
    /// the value is unchanged; otherwise stamps <see cref="UpdatedAt"/>.
    /// </summary>
    public void SetRole(Guid? teacherRoleCodedValueId)
    {
        if (TeacherRoleCodedValueId == teacherRoleCodedValueId) return;
        TeacherRoleCodedValueId = teacherRoleCodedValueId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
