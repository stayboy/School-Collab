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

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TeacherTopic Create(Guid teacherId, Guid topicId) =>
        new TeacherTopic
        {
            Id = Guid.NewGuid(),
            TeacherId = teacherId,
            TopicId = topicId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
}
