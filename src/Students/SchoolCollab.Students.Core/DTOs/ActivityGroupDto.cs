namespace SchoolCollab.Students.Core.DTOs;

public sealed record ActivityGroupDto(
    Guid Id,
    string Name,
    string? Description,
    string? Category,
    Guid? PeriodId,
    int? Capacity,
    string Status,
    int ActiveMemberCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
