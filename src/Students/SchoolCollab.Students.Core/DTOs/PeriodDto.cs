namespace SchoolCollab.Students.Core.DTOs;

public sealed record PeriodDto(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    string PeriodType,
    Guid? ParentPeriodId,
    Guid? NextPeriodId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);