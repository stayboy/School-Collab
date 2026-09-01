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

public sealed record PeriodDto(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    Guid? ParentPeriodId,
    Guid? NextPeriodId,
    string Division,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
