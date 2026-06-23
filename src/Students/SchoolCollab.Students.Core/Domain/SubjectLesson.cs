using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class SubjectLesson : IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private SubjectLesson() { }

    public Guid Id { get; private set; }
    public Guid SubjectId { get; private set; }
    public Subject Subject { get; private set; } = default!;
    public Guid? StrandId { get; private set; }
    public SubjectStrand? Strand { get; private set; }
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

    public static SubjectLesson Create(
        Guid subjectId,
        string name,
        string? description,
        DateOnly? startDate,
        DateOnly? endDate,
        int displayOrder)
    {
        if (startDate.HasValue && endDate.HasValue && endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));

        var now = DateTimeOffset.UtcNow;
        var lesson = new SubjectLesson
        {
            Id = Guid.NewGuid(),
            SubjectId = subjectId,
            Name = name.Trim(),
            Description = description?.Trim(),
            StartDate = startDate,
            EndDate = endDate,
            DisplayOrder = displayOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        lesson._domainEvents.Add(new SubjectLessonCreatedEvent(lesson.Id, lesson.Name, subjectId));
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
        _domainEvents.Add(new SubjectLessonUpdatedEvent(Id, Name));
    }

    public void SetStrand(Guid? strandId)
    {
        if (StrandId == strandId) return;
        StrandId = strandId;
        UpdatedAt = DateTimeOffset.UtcNow;
        if (strandId.HasValue)
        {
            _domainEvents.Add(new SubjectLessonStrandAssignedEvent(Id, strandId.Value));
        }
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}