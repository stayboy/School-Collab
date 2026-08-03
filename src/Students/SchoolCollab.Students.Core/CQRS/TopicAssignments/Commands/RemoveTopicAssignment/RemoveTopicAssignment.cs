using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.RemoveTopicAssignment;

public sealed record RemoveTopicAssignment(Guid Id) : ICommand;
