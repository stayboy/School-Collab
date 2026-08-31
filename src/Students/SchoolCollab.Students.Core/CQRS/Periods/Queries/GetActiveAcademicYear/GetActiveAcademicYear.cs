using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Periods.Queries.GetActiveAcademicYear;

/// <summary>
/// Returns the active top-level academic year (ParentPeriodId == null) as a
/// <see cref="PeriodDto"/>, or null when none is active
/// (period-hierarchy-terms-semesters.md FR-H12).
/// </summary>
public sealed record GetActiveAcademicYear : IQuery<PeriodDto?>;