using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.ArchivePeriod;

/// <summary>
/// Archives a period. For an AcademicYear, cascade-completes the year's
/// still-Active sub-periods BEFORE archiving the year (FR-H4b — no orphaned
/// Active sub-period behind a non-Active parent). Mirrors
/// <see cref="CompletePeriodHandler"/>'s cascade + cache-invalidation shape.
/// </summary>
public sealed class ArchivePeriodHandler(
    IPeriodRepository repository,
    HybridCache cache,
    ILogger<ArchivePeriodHandler> logger) : ICommandHandler<ArchivePeriod>
{
    public async Task HandleAsync(ArchivePeriod command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ArchivePeriod {Id}", command.Id);

        var period = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new PeriodNotFoundException(command.Id);

        // ── FR-H4b: parent-exit cascade. Archiving a top-level academic year must
        //    first complete its still-Active sub-periods, so no sub-period remains
        //    Active behind a non-Active parent.
        if (period.ParentPeriodId is null)
        {
            foreach (var sp in await repository.GetActiveSubPeriodsAsync(period.Id, cancellationToken: cancellationToken))
            {
                sp.Complete();
                sp.ClearDomainEvents();
            }
        }

        period.Archive();

        try
        {
            await repository.UpdateAsync(period, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Period", period.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        period.ClearDomainEvents();

        logger.LogInformation("Period {Id} archived", period.Id);
    }
}
