using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeachersForGradeLevel;

/// <summary>
/// Teachers linked to a grade level, each carrying their coded-value role on
/// that grade and the topics they teach (grade-level-detail-view-plan.md §3.1).
/// Inverse of <c>ListGradeLevelsForTeacher</c>.
/// </summary>
public sealed record ListTeachersForGradeLevel(Guid GradeLevelId) : IQuery<TeacherWithRoleDto[]>;
