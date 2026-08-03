using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.RemoveTopicStrand;

public sealed record RemoveTopicStrand(Guid Id) : ICommand;
