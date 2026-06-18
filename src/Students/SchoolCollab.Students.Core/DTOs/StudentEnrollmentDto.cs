namespace SchoolCollab.Students.Core.DTOs;

public sealed record StudentEnrollmentDto(
    Guid Id,
    Guid StudentId,
    Guid PeriodId,
    Guid GradeLevelId,
    DateOnly EnrolledOn,
    DateOnly? ExitDate,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);