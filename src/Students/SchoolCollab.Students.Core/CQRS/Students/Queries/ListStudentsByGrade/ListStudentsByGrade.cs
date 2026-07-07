using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Students.Queries.ListStudentsByGrade;

/// <summary>
/// Returns all students enrolled in a specific grade level for a given period.
/// If periodId is null, derives the current period server-side.
/// </summary>
public sealed record ListStudentsByGrade(
    Guid GradeLevelId,
    Guid? PeriodId = null) : IQuery<StudentDto[]>;