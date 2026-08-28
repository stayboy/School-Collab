using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Abstract base for a grade/activity-group ↔ topic bridge (TPH root). A topic is
/// a shared, global catalog definition; this bridge wires it to a specific
/// audience — a grade level or an activity group (see
/// subject-to-topic-polymorphism.md §2.4). The discriminator column
/// (<c>topic_assignment_type</c>) selects the concrete subtype
/// (<see cref="GradeTopicAssignment"/> or <see cref="ActivityGroupTopicAssignment"/>),
/// and each subtype carries its own non-nullable audience FK.
///
/// <para>The effective period is <b>date-based</b>, not period-bound: a topic stays
/// assigned to its audience across multiple years unless blocked/archived (a set
/// <c>EndDate</c>).</para>
/// </summary>
public abstract class TopicAssignment : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected TopicAssignment() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: tenant-owned (global-tenant-filter.md §3.2 Strict).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    /// <summary>The shared, global topic (subject) assigned to the grade/group.</summary>
    public Guid TopicId { get; private set; }

    /// <summary>
    /// First day the assignment is in effect. The assignment is open-ended
    /// (spans multiple years) while <see cref="EndDate"/> is null.
    /// </summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>
    /// Last day the assignment is in effect. Null = currently active /
    /// open-ended. A blocked or archived assignment has this set to a past or
    /// today's date, which ends its effective period. No status enum is needed:
    /// the effective window is fully expressed by <c>[StartDate, EndDate]</c>.
    /// </summary>
    public DateOnly? EndDate { get; private set; }
    public Guid? TopicStrandId { get; private set; }

    /// <summary>
    /// Rev. 6 FR-55: optional period scope. Null = the current date-based,
    /// year-spanning assignment; non-null = the topic is delivered during that
    /// specific academic-year/term/semester period (FR-56/57). Additive.
    /// </summary>
    public Guid? PeriodId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Seeds the shared state for a derived subtype. Validates the effective
    /// window and stamps the audit timestamps.
    /// </summary>
    protected void Initialize(
        Guid id,
        Guid topicId,
        DateOnly startDate,
        DateOnly? endDate,
        Guid? topicStrandId,
        Guid? periodId)
    {
        if (endDate is { } e && e < startDate)
            throw new ArgumentException("EndDate must be on or after StartDate.", nameof(endDate));

        var now = DateTimeOffset.UtcNow;
        Id = id;
        TopicId = topicId;
        StartDate = startDate;
        EndDate = endDate;
        TopicStrandId = topicStrandId;
        PeriodId = periodId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    protected void AddEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>
    /// True when the assignment is effective on <paramref name="date"/>: started
    /// on or before it and not ended before it (open-ended when <see cref="EndDate"/>
    /// is null).
    /// </summary>
    public bool IsEffectiveOn(DateOnly date) =>
        StartDate <= date && (EndDate is not { } end || end >= date);

    /// <summary>
    /// Ends the assignment's effective period on <paramref name="date"/>
    /// (blocking or archiving it). Calling on an already-ended assignment is a
    /// no-op. After this, <see cref="IsEffectiveOn"/> returns false for any date
    /// after <paramref name="date"/>.
    /// </summary>
    public void End(DateOnly date)
    {
        if (EndDate is { } end && end <= date) return;
        EndDate = date;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Sets the strand (root strand or lesson) this assignment pins for the topic.
    /// </summary>
    public void UpdateTags(Guid? strandId)
    {
        if (TopicStrandId == strandId) return;
        TopicStrandId = strandId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Changes the period scope of an existing assignment (Rev. 6 FR-55/56/57).
    /// Null reverts to the year-spanning (grade) / date-based-window (group)
    /// delivery. No-op when unchanged. This is the only mutation path for
    /// <see cref="PeriodId"/> after creation.
    /// </summary>
    public void UpdatePeriod(Guid? periodId)
    {
        if (PeriodId == periodId) return;
        PeriodId = periodId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
