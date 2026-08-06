using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeacherTopicRoles;

/// <summary>
/// The per-topic roles a teacher holds (grade-detail-rich-grids-plan.md §5 / cg/6).
/// Tenant-scoped. Used by the teacher create/edit dialog to prefill each topic's role
/// when editing an existing teacher.
/// </summary>
public sealed record ListTeacherTopicRoles(Guid TeacherId) : IQuery<SchoolCollab.Students.Core.DTOs.TeacherTopicRoleDto[]>;
