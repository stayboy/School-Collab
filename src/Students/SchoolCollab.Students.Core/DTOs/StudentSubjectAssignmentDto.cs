namespace SchoolCollab.Students.Core.DTOs;

public sealed record StudentSubjectAssignmentDto(
    Guid Id,
    Guid StudentId,
    Guid TopicId,
    Guid PeriodId,
    bool IsOverride,
    string SourceType,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);