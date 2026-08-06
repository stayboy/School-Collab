using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.UpdateTopic;

public sealed record UpdateTopic(
    Guid Id,
    string Name,
    int DisplayOrder,
    Guid? CodedValueId = null,
    string? Code = null) : ICommand;