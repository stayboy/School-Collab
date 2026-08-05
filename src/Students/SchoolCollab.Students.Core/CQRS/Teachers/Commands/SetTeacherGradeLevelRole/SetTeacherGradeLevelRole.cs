using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.SetTeacherGradeLevelRole;

/// <summary>
/// Sets or clears the coded-value role a teacher holds on a grade level
/// (grade-level-detail-view-plan.md §3.1). Idempotent at the domain layer.
/// </summary>
public sealed record SetTeacherGradeLevelRole(Guid TeacherId, Guid GradeLevelId, Guid? TeacherRoleCodedValueId) : ICommand;
