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

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.CompletePeriod;

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

        // ── H2.3 (FR-H10): completing an AcademicYear cascade-completes its
        //    still-Active sub-periods. Sub-period completion does NOT trigger
        //    promotion (grade enrollment is year-level), so no PeriodCompleted
        //    integration event is enqueued for them — only the year's.
        if (period.PeriodType == PeriodType.AcademicYear)
        {
            foreach (var sp in await repository.GetActiveSubPeriodsAsync(period.Id, cancellationToken: cancellationToken))
            {
                sp.Complete();
                sp.ClearDomainEvents();
            }
        }

        period.Complete();

        foreach (var evt in period.DomainEvents.OfType<PeriodCompletedEvent>())
        {
            await publisher.EnqueueAsync(new PeriodCompleted(
                period.Id,
                period.Name,
                DateTimeOffset.UtcNow), cancellationToken);
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

        logger.LogInformation("Period {Id} completed", period.Id);
    }
}