namespace SchoolCollab.Students.Core.DTOs;

public sealed record SubjectStrandDto(
    Guid Id,
    Guid SubjectId,
    string Name,
    string? Description,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
