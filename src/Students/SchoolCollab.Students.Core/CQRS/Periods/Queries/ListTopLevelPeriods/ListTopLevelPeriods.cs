using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Periods.Queries.ListTopLevelPeriods;

/// <summary>Lists top-level periods (academic years) only — sub-period rows are
/// excluded — with server-computed sub-period counts for the Periods landing
/// grid. The flat <see cref="ListPeriods"/> query stays available for consumers
/// that need the full hierarchy.</summary>
public sealed record ListTopLevelPeriods : IQuery<PeriodLandingDto[]>;