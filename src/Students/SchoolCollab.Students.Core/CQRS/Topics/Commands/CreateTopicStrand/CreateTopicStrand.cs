using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopicStrand;

public sealed record CreateTopicStrand(
    Guid TopicId,
    string Name,
    string? Description,
    int DisplayOrder) : ICommand;
