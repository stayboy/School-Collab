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
    /// Creates a bridge row assigning a topic to a grade level. The
    /// <see cref="TopicAssignment.TopicStrandId"/> selects which strand (or lesson,
    /// i.e. a parented strand) the grade uses for the topic.
    /// </summary>
    public static GradeTopicAssignment Create(
        Guid gradeLevelId,
        Guid topicId,
        DateOnly startDate,
        DateOnly? endDate = null,
        Guid? topicStrandId = null,
        Guid? periodId = null)
    {
        var assignment = new GradeTopicAssignment { GradeLevelId = gradeLevelId };
        assignment.Initialize(Guid.NewGuid(), topicId, startDate, endDate, topicStrandId, periodId);
        assignment.AddEvent(new GradeTopicAssignedEvent(
            assignment.Id, gradeLevelId, topicId, startDate, endDate));
        return assignment;
    }
}
