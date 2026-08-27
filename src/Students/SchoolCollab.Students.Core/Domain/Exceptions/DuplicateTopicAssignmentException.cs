namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when attempting to assign a topic to an activity group when the group
/// already holds an active (unended) assignment for the same
/// <c>(ActivityGroupId, TopicId, PeriodId)</c> (spec activity-group-enrollment.md
/// FR-56, Rev. 6). Mirrors <see cref="DuplicateActiveMembershipException"/> and
/// maps to HTTP 409 Conflict.
/// </summary>
public sealed class DuplicateTopicAssignmentException : Exception
{
    public Guid ActivityGroupId { get; }
    public Guid TopicId { get; }
    public Guid? PeriodId { get; }

    public DuplicateTopicAssignmentException(Guid activityGroupId, Guid topicId, Guid? periodId)
        : base($"Activity group '{activityGroupId}' already has an active assignment for topic '{topicId}'"
            + (periodId is null ? " (year-spanning)." : $" in period '{periodId}'."))
    {
        ActivityGroupId = activityGroupId;
        TopicId = topicId;
        PeriodId = periodId;
    }
}
