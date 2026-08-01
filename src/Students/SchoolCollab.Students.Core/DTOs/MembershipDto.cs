namespace SchoolCollab.Students.Core.DTOs;

public sealed record MembershipDto(
    Guid Id,
    Guid ActivityGroupId,
    Guid StudentId,
    string StudentName,
    DateOnly JoinedOn,
    DateOnly? ExitedOn,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
