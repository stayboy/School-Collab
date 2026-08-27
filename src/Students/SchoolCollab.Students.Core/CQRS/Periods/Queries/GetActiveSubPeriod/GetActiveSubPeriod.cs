using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Periods.Queries.GetActiveSubPeriod;

/// <summary>
/// Returns the active <c>Term</c>/<c>Semester</c> sub-period (within the active
/// academic year) as a <see cref="PeriodDto"/>, or null when none is active
/// (period-hierarchy-terms-semesters.md FR-H12).
/// </summary>
public sealed record GetActiveSubPeriod : IQuery<PeriodDto?>;