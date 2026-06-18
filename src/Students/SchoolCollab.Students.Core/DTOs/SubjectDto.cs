namespace SchoolCollab.Students.Core.DTOs;

public sealed record SubjectDto(
    Guid Id,
    Guid CodedValueId,
    string Code,
    string Name,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);