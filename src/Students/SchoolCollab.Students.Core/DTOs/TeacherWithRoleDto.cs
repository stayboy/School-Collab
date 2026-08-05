namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// A teacher linked to a grade level, carrying the optional coded-value role
/// they hold on that grade and the topics they teach (grade-level-detail-view-plan.md §3.2).
/// Returned by <c>ListTeachersForGradeLevel</c>.
/// </summary>
public sealed record TeacherWithRoleDto(
    Guid Id,
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    string Email,
    string? ContactPhone,
    bool IsDeleted,
    Guid? TeacherRoleCodedValueId,
    TopicDto[] AssignedTopics,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
