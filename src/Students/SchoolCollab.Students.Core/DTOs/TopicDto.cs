namespace SchoolCollab.Students.Core.DTOs;

public sealed record TopicDto(
    Guid Id,
    Guid? CodedValueId,
    string? Code,
    string Name,
    string? Description,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);