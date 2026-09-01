using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class PeriodRepository(StudentsDbContext db)
    : RepositoryBase<Period, StudentsDbContext>(db), IPeriodRepository
{
    public override async Task UpdateAsync(Period period, CancellationToken cancellationToken = default)
    {
        try
        {
            await Db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(period.Id);
        }
    }

    public async Task AddRangeAsync(IReadOnlyList<Period> periods, CancellationToken cancellationToken = default)
    {
        await Db.Periods.AddRangeAsync(periods, cancellationToken);
        await Db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PeriodDto[]> ListAsync(CancellationToken cancellationToken = default)
    {
        // Materialize first, then project in memory: the DTO projection uses the
        // null-propagating operator (Division?.ToString()), which is not allowed in
        // an EF expression tree (CS8072).
        var periods = await Db.Periods
            .AsNoTracking()
            .OrderByDescending(x => x.StartDate)
            .ToArrayAsync(cancellationToken);

        return periods.Select(x => new PeriodDto(
            x.Id, x.Name, x.StartDate, x.EndDate,
            x.Status.ToString(), x.ParentPeriodId, x.NextPeriodId,
            x.Division.ToString(), x.ActivationToleranceDays,
            x.CreatedAt, x.UpdatedAt)).ToArray();
    }

    public async Task<Period[]> GetActivePeriodsEndingBeforeAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        await Db.Periods
            .Where(x => x.Status == PeriodStatus.Active && x.EndDate < date)
            .ToArrayAsync(cancellationToken);

    public async Task<Period[]> GetOverlappingPeriodsAsync(
        DateOnly startDate,
        DateOnly endDate,
        Guid? excludeId = null,
        Guid? excludeParentId = null,
        Guid? excludeSubPeriodsOfParentId = null,
        CancellationToken cancellationToken = default)
        => await Db.Periods
            .Where(p => p.StartDate <= endDate
                && p.EndDate >= startDate
                && p.Status != PeriodStatus.Deactivated
                && (excludeId == null || p.Id != excludeId)
                && (excludeParentId == null || p.Id != excludeParentId)
                && (excludeSubPeriodsOfParentId == null || p.ParentPeriodId != excludeSubPeriodsOfParentId))
            .ToArrayAsync(cancellationToken);

    public async Task<Period?> GetActivePeriodAsync(Guid? excludeId = null, CancellationToken cancellationToken = default)
        => await Db.Periods
            .Where(p => p.Status == PeriodStatus.Active
                && (excludeId == null || p.Id != excludeId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Period?> GetActiveAcademicYearAsync(Guid? excludeId = null, CancellationToken cancellationToken = default)
        => await Db.Periods
            .Where(p => p.Status == PeriodStatus.Active
                && p.ParentPeriodId == null
                && (excludeId == null || p.Id != excludeId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Period[]> GetActiveSubPeriodsAsync(
        Guid parentPeriodId,
        AcademicYearDivision? division = null,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
        => await Db.Periods
            .Where(p => p.Status == PeriodStatus.Active
                && p.ParentPeriodId == parentPeriodId
                && (division == null || p.Division == division)
                && (excludeId == null || p.Id != excludeId))
            .ToArrayAsync(cancellationToken);

    public async Task<Period[]> GetSubPeriodsAsync(
        Guid parentPeriodId,
        CancellationToken cancellationToken = default)
        => await Db.Periods
            .Where(p => p.ParentPeriodId == parentPeriodId)
            .ToArrayAsync(cancellationToken);

    public async Task<Period[]> GetDraftPeriodsLinkedToAsync(Guid nextPeriodId, CancellationToken cancellationToken = default)
        => await Db.Periods
            .Where(p => p.NextPeriodId == nextPeriodId && p.Status == PeriodStatus.Draft)
            .ToArrayAsync(cancellationToken);

    public async Task<Period?> GetCurrentPeriodAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // "Current" = the active period containing today. Prefer the more specific
        // sub-period (Term/Semester) over the top-level academic year, then earliest
        // start — deterministic under the two-active-rows hierarchy.
        return await Db.Periods
            .AsNoTracking()
            .Where(p => p.Status == PeriodStatus.Active && p.StartDate <= today && p.EndDate >= today)
            .OrderBy(p => p.ParentPeriodId != null ? 0 : 1)
            .ThenBy(p => p.StartDate)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
