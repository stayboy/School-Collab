using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface IPeriodRepository
{
    Task<Period?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Period period, CancellationToken cancellationToken = default);
    Task UpdateAsync(Period period, CancellationToken cancellationToken = default);
    Task<PeriodDto[]> ListAsync(CancellationToken cancellationToken = default);
    Task<Period[]> GetActivePeriodsEndingBeforeAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every period whose [StartDate, EndDate] range intersects the given
    /// range (i.e. <c>p.StartDate &lt;= endDate && p.EndDate &gt;= startDate</c>),
    /// optionally excluding the period with <paramref name="excludeId"/> (self) and
    /// <paramref name="excludeParentId"/> (a sub-period's AcademicYear parent, whose
    /// range legally contains it). Used by the Create/Update handlers to enforce the
    /// no-overlap invariant (§5.6), which permits a sub-period inside its parent year
    /// but forbids sibling and cross-year overlap (FR-H3).
    /// </summary>
    Task<Period[]> GetOverlappingPeriodsAsync(
        DateOnly startDate,
        DateOnly endDate,
        Guid? excludeId = null,
        Guid? excludeParentId = null,
        Guid? excludeSubPeriodsOfParentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the currently <see cref="PeriodStatus.Active"/> period, excluding the
    /// one with <paramref name="excludeId"/> if provided. Used by the Activate handler
    /// to enforce "at most one active period" (§5.6).
    /// </summary>
    Task<Period?> GetActivePeriodAsync(Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active top-level academic year (ParentPeriodId == null), excluding
    /// <paramref name="excludeId"/> if provided (plan-drop-periodtype.md). Tracked,
    /// so completion is persisted by the handler's SaveChanges.
    /// </summary>
    Task<Period?> GetActiveAcademicYearAsync(Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active sub-periods (Term/Semester) of the given academic year,
    /// optionally restricted to <paramref name="division"/> and excluding
    /// <paramref name="excludeId"/> (FR-H4/H10). Tracked.
    /// </summary>
    Task<Period[]> GetActiveSubPeriodsAsync(
        Guid parentPeriodId,
        AcademicYearDivision? division = null,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the count of non-completed (Draft or Active) Term/Semester
    /// sub-periods of the given academic year. Used by the Update handler to
    /// reject a top-level year's division change while sub-periods exist
    /// (plan-drop-periodtype.md — the operator must complete/remove them first).
    /// </summary>
    Task<int> GetNonCompletedSubPeriodCountAsync(
        Guid parentPeriodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns ALL sub-periods (Term/Semester) of the given academic year, ANY
    /// status. Used by <c>ActivatePeriodHandler</c> (FR-H4a) to pick the earliest
    /// activatable sub-period of a newly activated year, and by
    /// <c>UpdatePeriodHandler</c> to reject a top-level year → sub-period flip that
    /// would orphan sub-periods. Tracked — the chosen sub-period is mutated and
    /// persisted by the handler's single SaveChanges.
    /// </summary>
    Task<Period[]> GetSubPeriodsAsync(
        Guid parentPeriodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the <b>current period</b> — the one whose
    /// <c>[StartDate, EndDate]</c> range contains today (UTC). This is the
    /// derived period used by landing-page queries and create-for-grade flows
    /// (§5.3, §8.1). Returns <c>null</c> if no period covers today.
    /// </summary>
    Task<Period?> GetCurrentPeriodAsync(CancellationToken cancellationToken = default);
}