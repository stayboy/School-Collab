using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
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

        // ── "At most one active period" invariant (§5.6 / FR-A1): opening this
        //    period MUST close any other currently-Active period for the tenant.
        //    Closing (Complete) is what triggers promotion/repetition — the
        //    PromotionService polls Completed periods that have a NextPeriodId.
        var activeOther = await repository.GetActivePeriodAsync(
            excludeId: command.Id, cancellationToken);
        if (activeOther is not null)
        {
            logger.LogInformation(
                "Closing prior active period {PriorId} ('{PriorName}') before activating {Id}",
                activeOther.Id, activeOther.Name, command.Id);
            activeOther.Complete();
        }

        period.Activate();

        try
        {
            await repository.UpdateAsync(period, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Period", period.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        foreach (var evt in period.DomainEvents.OfType<PeriodActivatedEvent>())
        {
            await publisher.EnqueueAsync(new PeriodActivated(
                period.Id,
                period.Name,
                period.StartDate,
                period.EndDate,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        period.ClearDomainEvents();

        logger.LogInformation("Period {Id} activated", period.Id);
    }
}
