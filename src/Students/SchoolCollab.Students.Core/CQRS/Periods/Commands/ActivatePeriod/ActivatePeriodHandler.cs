using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;

public sealed class ActivatePeriodHandler(
    IPeriodRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<ActivatePeriodHandler> logger) : ICommandHandler<ActivatePeriod>
{
    public async Task HandleAsync(ActivatePeriod command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ActivatePeriod {Id}", command.Id);

        var period = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new PeriodNotFoundException(command.Id);

        // ── Hierarchy-aware "at most one active" invariant (FR-H4, FR-H5):
        //    - Activating a top-level academic year closes the prior active year and
        //      cascade-completes its still-Active sub-periods.
        //    - Activating a Term/Semester requires its parent academic year to be
        //      Active (else PeriodNotOpenException) and closes the prior active
        //      sibling sub-period within that year.
        if (period.ParentPeriodId is null)
        {
            var priorYear = await repository.GetActiveAcademicYearAsync(
                excludeId: command.Id, cancellationToken);
            if (priorYear is not null)
            {
                logger.LogInformation(
                    "Closing prior active academic year {PriorId} ('{PriorName}') before activating {Id}",
                    priorYear.Id, priorYear.Name, command.Id);
                foreach (var sp in await repository.GetActiveSubPeriodsAsync(priorYear.Id, cancellationToken: cancellationToken))
                    sp.Complete();
                priorYear.Complete();
            }
        }
        else
        {
            // Sub-period: its parent academic year must be Active (FR-H5).
            var parent = await repository.GetAsync(period.ParentPeriodId!.Value, cancellationToken)
                ?? throw new PeriodNotFoundException(period.ParentPeriodId!.Value);
            if (parent.Status != PeriodStatus.Active)
            {
                throw new PeriodNotOpenException(
                    $"Cannot activate {period.Division} '{period.Name}': its academic year " +
                    $"'{parent.Name}' is not active.");
            }

            var priorSiblings = await repository.GetActiveSubPeriodsAsync(
                parent.Id, excludeId: command.Id, cancellationToken: cancellationToken);
            foreach (var sibling in priorSiblings)
            {
                logger.LogInformation(
                    "Closing prior active {Division} {PriorId} before activating {Id}",
                    sibling.Division, sibling.Id, command.Id);
                sibling.Complete();
            }
        }

        period.Activate();

        foreach (var evt in period.DomainEvents.OfType<PeriodActivatedEvent>())
        {
            await publisher.EnqueueAsync(new PeriodActivated(
                period.Id,
                period.Name,
                period.StartDate,
                period.EndDate,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        // ── FR-H4a: auto-activate the newly activated year's earliest sub-period
        //    so its current window is immediately available. Convenience, not an
        //    invariant — zero Active sub-periods stays valid (gap state, FR-H4); a
        //    None-division year activates none (NFR-H4). The sub-period is tracked
        //    (loaded via the repository) and persisted by the single SaveChanges
        //    below; its own PeriodActivated event is enqueued before the save.
        if (period.ParentPeriodId is null && period.Division != AcademicYearDivision.None)
        {
            var candidates = await repository.GetSubPeriodsAsync(period.Id, cancellationToken);
            var toActivate = candidates
                .Where(sp => sp.Status != PeriodStatus.Completed && sp.Status != PeriodStatus.Archived)
                .OrderBy(sp => sp.StartDate)
                .ThenBy(sp => sp.Id)
                .FirstOrDefault();
            if (toActivate is not null)
            {
                logger.LogInformation(
                    "Auto-activating sub-period {SubId} ('{SubName}') for newly activated academic year {Id}",
                    toActivate.Id, toActivate.Name, period.Id);
                toActivate.Activate();
                foreach (var evt in toActivate.DomainEvents.OfType<PeriodActivatedEvent>())
                {
                    await publisher.EnqueueAsync(new PeriodActivated(
                        toActivate.Id,
                        toActivate.Name,
                        toActivate.StartDate,
                        toActivate.EndDate,
                        DateTimeOffset.UtcNow), cancellationToken);
                }
                toActivate.ClearDomainEvents();
            }
        }

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

        logger.LogInformation("Period {Id} activated", period.Id);
    }
}
