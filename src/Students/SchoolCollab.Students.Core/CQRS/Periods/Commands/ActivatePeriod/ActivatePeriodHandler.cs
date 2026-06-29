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