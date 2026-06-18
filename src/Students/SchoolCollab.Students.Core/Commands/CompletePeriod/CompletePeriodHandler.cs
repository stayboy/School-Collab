using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Messaging;

namespace SchoolCollab.Students.Core.Commands.CompletePeriod;

public sealed class CompletePeriodHandler(
    IPeriodRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<CompletePeriodHandler> logger) : ICommandHandler<CompletePeriod>
{
    public async Task HandleAsync(CompletePeriod command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling CompletePeriod {Id}", command.Id);

        var period = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new PeriodNotFoundException(command.Id);

        period.Complete();

        try
        {
            await repository.UpdateAsync(period, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Period", period.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        foreach (var evt in period.DomainEvents.OfType<PeriodCompletedEvent>())
        {
            await publisher.EnqueueAsync(new PeriodCompleted(
                period.Id,
                period.Name,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        period.ClearDomainEvents();

        logger.LogInformation("Period {Id} completed", period.Id);
    }
}