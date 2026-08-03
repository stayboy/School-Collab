using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopic;

public sealed record CreateTopic(
    Guid CodedValueId,
    string Code,
    string Name,
    int DisplayOrder) : ICommand;