using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Queries.ListSubjectsByGrade;

/// <summary>
/// Returns all subjects assigned to a grade level for a given period.
/// If periodId is null, derives the current period server-side.
/// </summary>
public sealed record ListSubjectsByGrade(
    Guid GradeLevelId,
    Guid? PeriodId = null) : IQuery<SubjectDto[]>;