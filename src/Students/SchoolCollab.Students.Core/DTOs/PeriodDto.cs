namespace SchoolCollab.Students.Core.DTOs;

public sealed record PeriodDto(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    Guid? ParentPeriodId,
    Guid? NextPeriodId,
    string Division,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
