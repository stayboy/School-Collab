using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// A topic assignment targeting an <b>activity group</b> (TPH subtype of
/// <see cref="TopicAssignment"/>). <see cref="ActivityGroupId"/> is non-nullable;
/// grade-level topics use <see cref="GradeTopicAssignment"/>.
/// </summary>
public sealed class ActivityGroupTopicAssignment : TopicAssignment
{
    private ActivityGroupTopicAssignment() { }

    /// <summary>The activity group this topic is assigned to (always set).</summary>
    public Guid ActivityGroupId { get; private set; }

    /// <summary>
    /// Creates a bridge row assigning a topic to an activity group. The
    /// <see cref="TopicAssignment.TopicStrandId"/> selects which strand (or lesson,
    /// i.e. a parented strand) the group uses for the topic.
    /// </summary>
    public static ActivityGroupTopicAssignment Create(
        Guid activityGroupId,
        Guid topicId,
        DateOnly startDate,
        DateOnly? endDate = null,
        Guid? topicStrandId = null)
    {
        var assignment = new ActivityGroupTopicAssignment { ActivityGroupId = activityGroupId };
        assignment.Initialize(Guid.NewGuid(), topicId, startDate, endDate, topicStrandId);
        assignment.AddEvent(new ActivityGroupTopicAssignedEvent(
            assignment.Id, activityGroupId, topicId, startDate, endDate));
        return assignment;
    }
}
