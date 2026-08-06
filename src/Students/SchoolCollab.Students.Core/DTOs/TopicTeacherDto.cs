namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// A teacher linked to a topic, carrying the optional coded-value role they hold
/// <em>on that topic</em> (grade-detail-rich-grids-plan.md §5). Returned by
/// <c>ListTopicTeachers</c> and used by the grade Detail topic-teachers dialog.
/// </summary>
public sealed record TopicTeacherDto(
    Guid TeacherId,
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    Guid? RoleCodedValueId);
