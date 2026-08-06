using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// A shared, global topic (subject) definition. A topic is a tenant-scoped
/// catalog entry assigned to grades/groups via the M:N
/// <see cref="TopicAssignment"/> bridge. Per-grade/group strand and
/// lesson selection is expressed by the bridge's <c>TopicStrandId</c> and
/// <c>TopicLessonId</c> columns, not by duplicating the topic.
/// </summary>
public sealed class Topic : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private Topic() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: each row belongs to a tenant (global-tenant-filter.md §3.2 Strict).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    /// <summary>Optional; the operational peer of GradeLevel's coded value.</summary>
    public Guid? CodedValueId { get; private set; }

    // The following are kept for performance/indexing, but the source of truth
    // for metadata should be the CodedValue system + Tenant Overrides.
    public string? Code { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public int DisplayOrder { get; private set; }

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Creates a shared, global topic (subject) definition.
    /// </summary>
    public static Topic Create(
        Guid? codedValueId,
        string? code,
        string name,
        int displayOrder,
        string? description = null)
    {
        var now = DateTimeOffset.UtcNow;
        var topic = new Topic
        {
            Id = Guid.NewGuid(),
            CodedValueId = codedValueId,
            Code = string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            DisplayOrder = displayOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        topic._domainEvents.Add(new TopicCreatedEvent(topic.Id, topic.Code));
        return topic;
    }

    public void Update(string name, int displayOrder, string? description = null, Guid? codedValueId = null, string? code = null)
    {
        Name = name.Trim();
        DisplayOrder = displayOrder;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        // tcv/4: allow repointing the topic at a different CodedValue (e.g. a new
        // tenant-scoped provisional value) and syncing its denormalized code.
        if (codedValueId.HasValue) CodedValueId = codedValueId;
        if (!string.IsNullOrWhiteSpace(code)) Code = code.Trim().ToUpperInvariant();
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new TopicUpdatedEvent(Id, Code));
    }

    /// <summary>
    /// Marks the subject for deletion. The repository enforces referential
    /// integrity by checking for student-topic assignments before allowing
    /// the delete (see <c>DeleteTopicHandler</c>).
    /// </summary>
    /// <exception cref="TopicReferencedException">
    /// Thrown if student-topic assignments reference this topic.
    /// </exception>
    public void Delete()
    {
        // Delete is a hard delete. The repository enforces referential integrity
        // by checking for StudentTopicAssignments before allowing the delete.
        // See DeleteTopicHandler.
        _domainEvents.Add(new TopicDeletedEvent(Id, Code));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}