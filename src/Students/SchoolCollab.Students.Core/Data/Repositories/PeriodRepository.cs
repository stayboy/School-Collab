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
                x.Status.ToString(), x.AllowSubjectOverrides, x.NextPeriodId,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<Period[]> GetActivePeriodsEndingBeforeAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        await Db.Periods
            .Where(x => x.Status == PeriodStatus.Active && x.EndDate < date)
            .ToArrayAsync(cancellationToken);
}
