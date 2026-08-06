namespace SchoolCollab.Students.Core.DTOs;

/// <summary>A teacher (spec §4.12). Referenced by Assignments.Core via <c>Guid</c>.</summary>
public sealed record TeacherDto(
    Guid Id,
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    Guid? GenderCodedValueId,
    DateOnly? DateOfBirth,
    Guid? LevelOfEducationCodedValueId,
    Guid[] QualificationCodedValueIds,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
