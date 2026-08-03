using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.UpdateTopicStrand;

public sealed record UpdateTopicStrand(
    Guid Id,
    string Name,
    string? Description,
    int DisplayOrder) : ICommand;
