using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Link between a teacher and a topic they teach (spec §4.12). Subject->Topic rename (FR-13).
/// </summary>
public sealed class TeacherTopic : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private TeacherTopic() { }

    public Guid Id { get; private set; }
    public Guid TeacherId { get; private set; }
    public Guid TopicId { get; private set; }

    /// <summary>
    /// Optional coded-value role the teacher holds <em>on this topic</em> (e.g.
    /// Head of Department, Subject Lead) — mirrors <see cref="TeacherGradeLevel"/>.
    /// Reuses the <c>TeacherRoles</c>/<c>TCHROLES</c> parent.
    /// </summary>
    public Guid? RoleCodedValueId { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TeacherTopic Create(Guid teacherId, Guid topicId, Guid? roleCodedValueId = null) =>
        new TeacherTopic
        {
            Id = Guid.NewGuid(),
            TeacherId = teacherId,
            TopicId = topicId,
            RoleCodedValueId = roleCodedValueId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    public void SetRole(Guid? roleCodedValueId)
    {
        if (RoleCodedValueId == roleCodedValueId) return;
        RoleCodedValueId = roleCodedValueId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
