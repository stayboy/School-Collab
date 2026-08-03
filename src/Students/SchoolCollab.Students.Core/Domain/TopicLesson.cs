using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class TopicLesson : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private TopicLesson() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: inherits the topic's tenant (global-tenant-filter.md §3.2 Strict).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    public Guid TopicId { get; private set; }
    public Topic Topic { get; private set; } = default!;
    public Guid? StrandId { get; private set; }
    public TopicStrand? Strand { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public bool IsOpenEnded => !StartDate.HasValue || !EndDate.HasValue;
    public int DisplayOrder { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static TopicLesson Create(
        Guid topicId,
        string name,
        string? description,
        DateOnly? startDate,
        DateOnly? endDate,
        int displayOrder)
    {
        if (startDate.HasValue && endDate.HasValue && endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));

        var now = DateTimeOffset.UtcNow;
        var lesson = new TopicLesson
        {
            Id = Guid.NewGuid(),
            TopicId = topicId,
            Name = name.Trim(),
            Description = description?.Trim(),
            StartDate = startDate,
            EndDate = endDate,
            DisplayOrder = displayOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        lesson._domainEvents.Add(new TopicLessonCreatedEvent(lesson.Id, lesson.Name, topicId));
        return lesson;
    }

    public void Update(
        string name,
        string? description,
        DateOnly? startDate,
        DateOnly? endDate,
        int displayOrder)
    {
        if (startDate.HasValue && endDate.HasValue && endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));

        Name = name.Trim();
        Description = description?.Trim();
        StartDate = startDate;
        EndDate = endDate;
        DisplayOrder = displayOrder;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new TopicLessonUpdatedEvent(Id, Name));
    }

    public void SetStrand(Guid? strandId)
    {
        if (StrandId == strandId) return;
        StrandId = strandId;
        UpdatedAt = DateTimeOffset.UtcNow;
        if (strandId.HasValue)
        {
            _domainEvents.Add(new TopicLessonStrandAssignedEvent(Id, strandId.Value));
        }
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}