using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.UpdateTopicAssignmentTags;

public sealed record UpdateTopicAssignmentTags(
    Guid AssignmentId,
    Guid? TopicStrandId) : ICommand;
