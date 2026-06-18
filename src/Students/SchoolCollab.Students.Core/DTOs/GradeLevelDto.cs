namespace SchoolCollab.Students.Core.DTOs;

public sealed record GradeLevelDto(
    Guid Id,
    Guid CodedValueId,
    int Level,
    string Name,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);