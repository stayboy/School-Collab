using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignActivityGroupTopic;

public sealed record AssignActivityGroupTopic(
    Guid ActivityGroupId,
    Guid TopicId,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    Guid? TopicStrandId = null,
    Guid? TopicLessonId = null) : ICommand;
