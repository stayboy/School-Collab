namespace SchoolCollab.Students.Core.DTOs;

public sealed record GuardianNameHistoryDto(
    Guid Id,
    Guid GuardianId,
    string FirstName,
    string LastName,
    string? DisplayName,
    DateTimeOffset CreatedAt);
