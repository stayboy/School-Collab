using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Commands.RemoveStudentTopic;

public sealed record RemoveStudentTopic(Guid Id) : ICommand;