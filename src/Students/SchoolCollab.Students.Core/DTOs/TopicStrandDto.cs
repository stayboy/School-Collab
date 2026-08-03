namespace SchoolCollab.Students.Core.DTOs;

public sealed record TopicStrandDto(
    Guid Id,
    Guid TopicId,
    string Name,
    string? Description,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
