using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Periods.Queries.GetSubPeriodCount;

/// <summary>
/// Returns the count of non-completed <c>Term</c>/<c>Semester</c> sub-periods
/// (Draft or Active) for the current tenant. Consumed by the Settings context's
/// academic-year-division switch-rejection (FR-H7): a framework change is
/// rejected while sub-periods exist, so the tenant must complete/remove them
/// first.
/// </summary>
public sealed record GetSubPeriodCount : IQuery<SubPeriodCountDto>;

/// <summary>Result DTO carrying the non-completed sub-period count.</summary>
public sealed record SubPeriodCountDto(int Count);