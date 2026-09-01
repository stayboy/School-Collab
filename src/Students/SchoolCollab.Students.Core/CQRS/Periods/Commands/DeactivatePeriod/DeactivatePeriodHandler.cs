using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.DeactivatePeriod;

public sealed class DeactivatePeriodHandler(
    IPeriodRepository repository,
    HybridCache cache,
    ILogger<DeactivatePeriodHandler> logger) : ICommandHandler<DeactivatePeriod>
{
    public async Task HandleAsync(DeactivatePeriod command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling DeactivatePeriod {Id}", command.Id);

        // FR-X10 / NFR-E2: the tenant query filter makes unknown/other-tenant rows null
        // here -> 404 (AC-E9).
        var period = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new PeriodNotFoundException(command.Id);

        // FR-X1: Active-only guard -> PeriodNotDeactivatableException (422). No idempotent
        // early-return: deactivating an already-Deactivated period is a 422 (AC-E8), not a
        // no-op.
        period.Deactivate();

        // FR-X2 / AC-E5: a top-level year deactivates its still-Active sub-periods too, so
        // no Active orphans remain under a Deactivated parent. All sub-periods are tracked,
        // so the single UpdateAsync SaveChanges below persists the whole cascade atomically
        // (NFR-E1). Guard runs first (only Active subs matched), then all mutate, then one
        // save.
        if (period.ParentPeriodId is null)
        {
            foreach (var sp in await repository.GetActiveSubPeriodsAsync(period.Id, cancellationToken: cancellationToken))
                sp.Deactivate();
        }

        // Persists the year + cascaded sub-periods in one SaveChanges (NFR-E1);
        // UpdateAsync maps DbUpdateConcurrencyException -> ConcurrencyException (NFR-E2).
        await repository.UpdateAsync(period, cancellationToken);

        await cache.RemoveByTagAsync("students", cancellationToken);

        period.ClearDomainEvents();

        logger.LogInformation("Period {Id} deactivated", period.Id);
    }
}
