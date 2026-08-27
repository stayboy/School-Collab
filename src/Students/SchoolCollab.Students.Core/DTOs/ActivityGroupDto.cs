namespace SchoolCollab.Students.Core.DTOs;

public sealed record ActivityGroupDto(
    Guid Id,
    string Name,
    string? Description,
    string? Category,
    int? Capacity,
    bool IsActive,
    string Span,
    DateOnly? EnrollmentStartDate,
    DateOnly? EnrollmentEndDate,
    bool AutoRenewDefault,
    Guid[] EligibleGradeIds,
    int ActiveMemberCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);