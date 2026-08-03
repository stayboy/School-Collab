using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.RemoveTopicLesson;

public sealed record RemoveTopicLesson(Guid Id) : ICommand;
