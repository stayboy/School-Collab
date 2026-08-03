namespace SchoolCollab.Students.Core.DTOs;

public sealed record TopicLessonDto(
    Guid Id,
    Guid TopicId,
    Guid? StrandId,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool IsOpenEnded,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
