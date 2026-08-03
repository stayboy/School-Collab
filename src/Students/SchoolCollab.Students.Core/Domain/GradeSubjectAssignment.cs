using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class GradeSubjectAssignment : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private GradeSubjectAssignment() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: tenant-owned (global-tenant-filter.md §3.2 Strict).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    /// <summary>Grade level this topic is assigned to (null for activity-group topics).</summary>
    public Guid? GradeLevelId { get; private set; }

    /// <summary>Activity group this topic is assigned to (null for grade-level topics).</summary>
    public Guid? ActivityGroupId { get; private set; }

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
    public Guid? TopicLessonId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Creates a bridge row assigning a topic to a grade level (or, if
    /// <paramref name="gradeLevelId"/> is null, to an activity group). The
    /// <see cref="TopicStrandId"/>/<see cref="TopicLessonId"/> select which
    /// strand/lesson the grade/group uses for the topic.
    /// </summary>
    /// <param name="startDate">First effective day (required).</param>
    /// <param name="endDate">
    /// Last effective day. Omit (or pass null) to keep the assignment
    /// open-ended/active; set it to end the assignment (block/archive).
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="endDate"/> is set but precedes
    /// <paramref name="startDate"/>.
    /// </exception>
    public static GradeSubjectAssignment Create(
        Guid? gradeLevelId,
        Guid? activityGroupId,
        Guid topicId,
        DateOnly startDate,
        DateOnly? endDate = null,
        Guid? topicStrandId = null,
        Guid? topicLessonId = null)
    {
        if (endDate is { } e && e < startDate)
            throw new ArgumentException("EndDate must be on or after StartDate.", nameof(endDate));

        var now = DateTimeOffset.UtcNow;
        var assignment = new GradeSubjectAssignment
        {
            Id = Guid.NewGuid(),
            GradeLevelId = gradeLevelId,
            ActivityGroupId = activityGroupId,
            TopicId = topicId,
            StartDate = startDate,
            EndDate = endDate,
            TopicStrandId = topicStrandId,
            TopicLessonId = topicLessonId,
            CreatedAt = now,
            UpdatedAt = now
        };

        assignment._domainEvents.Add(new GradeTopicAssignedEvent(assignment.Id, gradeLevelId, activityGroupId, topicId, startDate, endDate));
        return assignment;
    }

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

    public void UpdateTags(Guid? strandId, Guid? lessonId)
    {
        if (TopicStrandId == strandId && TopicLessonId == lessonId) return;
        
        TopicStrandId = strandId;
        TopicLessonId = lessonId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}