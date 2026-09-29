namespace SchoolCollab.Students.Core.DTOs;

/// <summary>One top-level period (academic year) row for the Periods landing
/// grid. Unlike <see cref="PeriodDto"/> this carries server-computed sub-period
/// counts, so the UI needs no per-row sub-period fetches and no sub-period rows
/// are sent for display.</summary>
public sealed record PeriodLandingDto(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    string Division,
    int SubPeriodCount,
    int DraftSubPeriodCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One period (academic year or sub-period) as the UI consumes it.
///
/// <para><see cref="Sequence"/> is declared <b>last and optional</b> rather than beside
/// <see cref="Division"/>, where it belongs semantically: this record is constructed
/// positionally in six query handlers, two repositories and several tests, and inserting a
/// parameter mid-record would break every one of them for a field that is a nullable
/// addition. It carries the 1-based position of a sub-period within its year's run of the
/// same division (subject-period-exception-model.md v5 §0 decision 15); it is null for a
/// top-level academic year and for a sub-period whose position the tenant has not
/// declared.</para>
/// </summary>
public sealed record PeriodDto(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    Guid? ParentPeriodId,
    Guid? NextPeriodId,
    string Division,
    int? ActivationToleranceDays,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? Sequence = null);
