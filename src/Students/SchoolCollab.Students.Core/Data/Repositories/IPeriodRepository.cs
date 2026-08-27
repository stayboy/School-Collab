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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the currently <see cref="PeriodStatus.Active"/> period, excluding the
    /// one with <paramref name="excludeId"/> if provided. Used by the Activate handler
    /// to enforce "at most one active period" (§5.6).
    /// </summary>
    Task<Period?> GetActivePeriodAsync(Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active <see cref="PeriodType.AcademicYear"/> period, excluding
    /// <paramref name="excludeId"/> if provided (period-hierarchy-terms-semesters.md
    /// FR-H4). Tracked, so completion is persisted by the handler's SaveChanges.
    /// </summary>
    Task<Period?> GetActiveAcademicYearAsync(Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active sub-periods (Term/Semester) of the given academic year,
    /// optionally restricted to <paramref name="periodType"/> and excluding
    /// <paramref name="excludeId"/> (FR-H4/H10). Tracked.
    /// </summary>
    Task<Period[]> GetActiveSubPeriodsAsync(
        Guid parentPeriodId,
        PeriodType? periodType = null,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the <b>current period</b> — the one whose
    /// <c>[StartDate, EndDate]</c> range contains today (UTC). This is the
    /// derived period used by landing-page queries and create-for-grade flows
    /// (§5.3, §8.1). Returns <c>null</c> if no period covers today.
    /// </summary>
    Task<Period?> GetCurrentPeriodAsync(CancellationToken cancellationToken = default);
}