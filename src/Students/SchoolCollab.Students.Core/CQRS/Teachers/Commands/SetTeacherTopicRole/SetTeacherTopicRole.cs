using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.SetTeacherTopicRole;

/// <summary>
/// Sets or clears the coded-value role a teacher holds on a topic
/// (grade-detail-rich-grids-plan.md §5). Idempotent at the domain layer.
/// </summary>
public sealed record SetTeacherTopicRole(Guid TeacherId, Guid TopicId, Guid? RoleCodedValueId, DateOnly? StartDate = null, DateOnly? EndDate = null) : ICommand;
