namespace SchoolCollab.Students.Core.DTOs;

public sealed record StudentDto(
    Guid Id,
    string StudentNumber,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    string ContactEmail,
    string? ContactPhone,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);