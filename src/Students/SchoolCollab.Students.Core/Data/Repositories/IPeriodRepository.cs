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
}