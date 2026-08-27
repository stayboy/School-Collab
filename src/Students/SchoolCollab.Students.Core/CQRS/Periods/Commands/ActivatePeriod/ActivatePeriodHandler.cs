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
        //    - Activating an AcademicYear closes the prior active AcademicYear and
        //      cascade-completes its still-Active sub-periods.
        //    - Activating a Term/Semester requires its parent AcademicYear to be
        //      Active (else PeriodNotOpenException) and closes the prior active
        //      sibling sub-period of the same type within that year.
        if (period.PeriodType == PeriodType.AcademicYear)
        {
            var priorYear = await repository.GetActiveAcademicYearAsync(
                excludeId: command.Id, cancellationToken);
            if (priorYear is not null)
            {
                logger.LogInformation(
                    "Closing prior active AcademicYear {PriorId} ('{PriorName}') before activating {Id}",
                    priorYear.Id, priorYear.Name, command.Id);
                foreach (var sp in await repository.GetActiveSubPeriodsAsync(priorYear.Id, cancellationToken: cancellationToken))
                    sp.Complete();
                priorYear.Complete();
            }
        }
        else
        {
            // Sub-period: its parent AcademicYear must be Active (FR-H5).
            var parent = await repository.GetAsync(period.ParentPeriodId!.Value, cancellationToken)
                ?? throw new PeriodNotFoundException(period.ParentPeriodId!.Value);
            if (parent.Status != PeriodStatus.Active)
            {
                throw new PeriodNotOpenException(
                    $"Cannot activate {period.PeriodType} '{period.Name}': its AcademicYear " +
                    $"'{parent.Name}' is not active.");
            }

            var priorSiblings = await repository.GetActiveSubPeriodsAsync(
                parent.Id, period.PeriodType, excludeId: command.Id, cancellationToken);
            foreach (var sibling in priorSiblings)
            {
                logger.LogInformation(
                    "Closing prior active {Type} {PriorId} before activating {Id}",
                    sibling.PeriodType, sibling.Id, command.Id);
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
