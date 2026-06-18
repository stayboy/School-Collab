using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class PeriodRepository(StudentsDbContext db) : IPeriodRepository
{
    public Task<Period?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Periods.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task AddAsync(Period period, CancellationToken cancellationToken = default)
    {
        await db.Periods.AddAsync(period, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Period period, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(period.Id);
        }
    }

    public async Task<PeriodDto[]> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Periods
            .AsNoTracking()
            .OrderByDescending(x => x.StartDate)
            .Select(x => new PeriodDto(
                x.Id, x.Name, x.StartDate, x.EndDate,
                x.Status.ToString(), x.AllowSubjectOverrides, x.NextPeriodId,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<Period[]> GetActivePeriodsEndingBeforeAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        await db.Periods
            .Where(x => x.Status == PeriodStatus.Active && x.EndDate < date)
            .ToArrayAsync(cancellationToken);
}