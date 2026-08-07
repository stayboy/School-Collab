using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopicLesson;

public sealed record CreateTopicLesson(
    Guid TopicId,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int DisplayOrder,
    Guid? StrandId = null) : ICommand;
