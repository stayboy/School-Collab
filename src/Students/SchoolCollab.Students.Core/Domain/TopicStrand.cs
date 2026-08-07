using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class TopicStrand : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private TopicStrand() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: inherits the topic's tenant (global-tenant-filter.md §3.2 Strict).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    public Guid TopicId { get; private set; }
    public Topic Topic { get; private set; } = default!;

    /// <summary>
    /// The parent (root) strand for this strand. A strand with a parent is a
    /// <b>lesson</b>; a strand without one is a top-level strand. Parents must be
    /// root strands (no parent), belong to the same topic, and never be itself —
    /// strand-lesson-unification-plan.md.
    /// </summary>
    public Guid? ParentStrandId { get; private set; }
    public TopicStrand? Parent { get; private set; }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>Optional scheduling window, meaningful for lessons (parented strands).</summary>
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public bool IsOpenEnded => !StartDate.HasValue || !EndDate.HasValue;

    public int DisplayOrder { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { private set; get; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>True when this strand is a lesson (has a parent strand).</summary>
    public bool IsLesson => ParentStrandId.HasValue;

    public static TopicStrand Create(
        Guid topicId,
        string name,
        string? description,
        int displayOrder,
        Guid? parentStrandId = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null)
    {
        ValidateDates(startDate, endDate);
        if (parentStrandId == Guid.Empty)
            throw new ArgumentException("Parent strand cannot be empty.", nameof(parentStrandId));

        var now = DateTimeOffset.UtcNow;
        var strand = new TopicStrand
        {
            Id = Guid.NewGuid(),
            TopicId = topicId,
            ParentStrandId = parentStrandId,
            Name = name.Trim(),
            Description = description?.Trim(),
            StartDate = startDate,
            EndDate = endDate,
            DisplayOrder = displayOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        strand._domainEvents.Add(new TopicStrandCreatedEvent(strand.Id, strand.Name, topicId));
        return strand;
    }

    public void Update(
        string name,
        string? description,
        int displayOrder,
        Guid? parentStrandId = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null)
    {
        ValidateDates(startDate, endDate);
        if (parentStrandId == Guid.Empty)
            throw new ArgumentException("Parent strand cannot be empty.", nameof(parentStrandId));

        Name = name.Trim();
        Description = description?.Trim();
        ParentStrandId = parentStrandId;
        StartDate = startDate;
        EndDate = endDate;
        DisplayOrder = displayOrder;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new TopicStrandUpdatedEvent(Id, Name));
    }

    /// <summary>Re-parents this strand (a strand with a parent is a lesson).</summary>
    public void SetParent(Guid? parentStrandId)
    {
        if (ParentStrandId == parentStrandId) return;
        ParentStrandId = parentStrandId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateDates(DateOnly? startDate, DateOnly? endDate)
    {
        if (startDate.HasValue && endDate.HasValue && endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}