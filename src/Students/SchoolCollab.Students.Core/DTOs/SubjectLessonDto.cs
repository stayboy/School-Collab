namespace SchoolCollab.Students.Core.DTOs;

public sealed record SubjectLessonDto(
    Guid Id,
    Guid SubjectId,
    Guid? StrandId,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool IsOpenEnded,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
