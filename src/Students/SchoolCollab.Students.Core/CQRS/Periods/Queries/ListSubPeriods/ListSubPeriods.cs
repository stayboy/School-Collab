using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Periods.Queries.ListSubPeriods;

/// <summary>
/// Returns the sub-periods (children) of a given academic year as
/// <see cref="PeriodDto"/>, ordered by start date
/// (period-hierarchy-terms-semesters.md FR-H12).
/// </summary>
public sealed record ListSubPeriods(Guid AcademicYearId) : IQuery<PeriodDto[]>;