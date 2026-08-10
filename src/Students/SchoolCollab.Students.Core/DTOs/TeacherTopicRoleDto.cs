namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// The role a teacher holds on a topic (grade-detail-rich-grids-plan.md §5 / cg/6).
/// Returned by <c>ListTeacherTopicRoles</c> — the inverse of <c>TopicTeacherDto</c>,
/// used by the teacher create/edit dialog to prefill per-topic roles when editing.
/// </summary>
public sealed record TeacherTopicRoleDto(
    Guid TopicId,
    Guid? RoleCodedValueId = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null);
