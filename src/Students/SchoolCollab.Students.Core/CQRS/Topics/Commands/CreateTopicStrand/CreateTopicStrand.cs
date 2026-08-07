using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopicStrand;

public sealed record CreateTopicStrand(
    Guid TopicId,
    string Name,
    string? Description,
    int DisplayOrder,
    Guid? ParentStrandId = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null) : ICommand;
