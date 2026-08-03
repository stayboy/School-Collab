using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.DeleteTopic;

public sealed record DeleteTopic(Guid Id) : ICommand;