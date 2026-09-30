using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// A topic assignment targeting a <b>grade level</b> (TPH subtype of
/// <see cref="TopicAssignment"/>). <see cref="GradeLevelId"/> is non-nullable;
/// activity-group topics use <see cref="ActivityGroupTopicAssignment"/>.
/// </summary>
public sealed class GradeTopicAssignment : TopicAssignment
{
    private GradeTopicAssignment() { }

    /// <summary>The grade level this topic is assigned to (always set).</summary>
    public Guid GradeLevelId { get; private set; }

    /// <summary>
    /// Position of this subject in the grade's curriculum list. The <b>bridge
    /// row</b> is the ordering authority — <see cref="Topic.DisplayOrder"/> orders
    /// the shared, cross-grade subject catalogue, a different concern. The column
    /// lives on the shared <c>topic_assignments</c> table (TPH) and is null for
    /// <see cref="ActivityGroupTopicAssignment"/> rows, which have no order
    /// surface. Contiguous 0..n-1 per grade once normalised; the migration
    /// backfills it from the listing order the UI rendered before the column
    /// existed.
    /// </summary>
    public int DisplayOrder { get; private set; }

    /// <summary>
    /// Creates a bridge row assigning a topic to a grade level. The
    /// <see cref="TopicAssignment.TopicStrandId"/> selects which strand (or lesson,
    /// i.e. a parented strand) the grade uses for the topic.
    /// <paramref name="displayOrder"/> defaults to 0 so existing callers keep
    /// compiling; the create flows stamp the append-at-end position explicitly.
    /// </summary>
    public static GradeTopicAssignment Create(
        Guid gradeLevelId,
        Guid topicId,
        DateOnly startDate,
        DateOnly? endDate = null,
        Guid? topicStrandId = null,
        Guid? periodId = null,
        int displayOrder = 0)
    {
        var assignment = new GradeTopicAssignment { GradeLevelId = gradeLevelId, DisplayOrder = displayOrder };
        assignment.Initialize(Guid.NewGuid(), topicId, startDate, endDate, topicStrandId, periodId);
        assignment.AddEvent(new GradeTopicAssignedEvent(
            assignment.Id, gradeLevelId, topicId, startDate, endDate));
        return assignment;
    }

    /// <summary>
    /// Moves this subject to <paramref name="displayOrder"/>. The reorder handler
    /// owns the swap semantics (normalise, then swap the mover with the row at the
    /// requested position); this only stamps the value and the audit timestamp.
    /// </summary>
    public void SetDisplayOrder(int displayOrder)
    {
        if (DisplayOrder == displayOrder) return;
        DisplayOrder = displayOrder;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
