namespace SchoolCollab.Students.Core.DTOs;

public sealed record GradeLevelDto(
    Guid Id,
    Guid CodedValueId,
    int Level,
    string Name,
    int DisplayOrder,
    int TopicCount,
    int StudentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? MinAge = null,
    int? MaxAge = null,
    Guid? AllowedGenderCodedValueId = null,
    bool IsBlockedFromEnrollment = false);