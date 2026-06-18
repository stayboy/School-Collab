namespace SchoolCollab.Students.Core.DTOs;

public sealed record PeriodDto(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    bool AllowSubjectOverrides,
    Guid? NextPeriodId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);