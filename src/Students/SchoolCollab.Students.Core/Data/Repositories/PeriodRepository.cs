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

    public async Task<PeriodDto[]> ListAsync(CancellationToken cancellationToken = default) =>
        await Db.Periods
            .AsNoTracking()
            .OrderByDescending(x => x.StartDate)
            .Select(x => new PeriodDto(
                x.Id, x.Name, x.StartDate, x.EndDate,
                x.Status.ToString(), x.PeriodType.ToString(), x.ParentPeriodId, x.NextPeriodId,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<Period[]> GetActivePeriodsEndingBeforeAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        await Db.Periods
            .Where(x => x.Status == PeriodStatus.Active && x.EndDate < date)
            .ToArrayAsync(cancellationToken);

    public async Task<Period[]> GetOverlappingPeriodsAsync(
        DateOnly startDate,
        DateOnly endDate,
        Guid? excludeId = null,
        Guid? excludeParentId = null,
        CancellationToken cancellationToken = default)
        => await Db.Periods
            .Where(p => p.StartDate <= endDate
                && p.EndDate >= startDate
                && (excludeId == null || p.Id != excludeId)
                && (excludeParentId == null || p.Id != excludeParentId))
            .ToArrayAsync(cancellationToken);

    public async Task<Period?> GetActivePeriodAsync(Guid? excludeId = null, CancellationToken cancellationToken = default)
        => await Db.Periods
            .Where(p => p.Status == PeriodStatus.Active
                && (excludeId == null || p.Id != excludeId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Period?> GetActiveAcademicYearAsync(Guid? excludeId = null, CancellationToken cancellationToken = default)
        => await Db.Periods
            .Where(p => p.Status == PeriodStatus.Active
                && p.PeriodType == PeriodType.AcademicYear
                && (excludeId == null || p.Id != excludeId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Period[]> GetActiveSubPeriodsAsync(
        Guid parentPeriodId,
        PeriodType? periodType = null,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
        => await Db.Periods
            .Where(p => p.Status == PeriodStatus.Active
                && p.ParentPeriodId == parentPeriodId
                && (periodType == null || p.PeriodType == periodType)
                && (excludeId == null || p.Id != excludeId))
            .ToArrayAsync(cancellationToken);

    public async Task<Period?> GetCurrentPeriodAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await Db.Periods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.StartDate <= today && p.EndDate >= today, cancellationToken);
    }
}
