using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
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
    ILogger<ActivatePeriodHandler> logger,
    IConfiguration configuration) : ICommandHandler<ActivatePeriod>
{
    public async Task HandleAsync(ActivatePeriod command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ActivatePeriod {Id}", command.Id);

        var period = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new PeriodNotFoundException(command.Id);

        // ── Activation-window guard (period-activation-window-auto-activation.md FR-W1/W2/W4):
        //    a period whose [StartDate, EndDate] is far away from today cannot be activated.
        //    Effective tolerance = per-period override (ActivationToleranceDays) or the global
        //    default (Students:PeriodActivationToleranceDays, default 10). Evaluated BEFORE any
        //    state mutation (before the FR-G1 sub-period guard, prior-year close, sibling close,
        //    and Activate()) so a guard failure leaves zero rows changed (all-or-nothing).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var defaultTolerance = Math.Max(0, configuration.GetValue("Students:PeriodActivationToleranceDays", defaultValue: 10));
        if (!period.IsWithinActivationWindow(today, defaultTolerance))
        {
            var tolerance = period.ActivationToleranceDays ?? defaultTolerance;
            var toleranceSource = period.ActivationToleranceDays is { } overrideDays
                ? $"per-period override ({overrideDays} days)"
                : $"global default ({defaultTolerance} days)";
            throw new PeriodActivationWindowException(
                $"Cannot activate period '{period.Name}' ({period.StartDate:O}–{period.EndDate:O}): " +
                $"today ({today:O}) is outside the activation window " +
                $"[{period.StartDate.AddDays(-tolerance):O}, {period.EndDate.AddDays(tolerance):O}] " +
                $"(tolerance {tolerance} days, {toleranceSource}).");
        }

        // ── Activation guard (period-activation-guard-atomic-create.md FR-G1/G2):
        //    a top-level academic year divided into Terms/Semesters cannot be
        //    activated until it has at least one Draft sub-period (a sub-period
        //    that Activate() can transition). Evaluated BEFORE any state mutation
        //    so a guard failure leaves zero rows changed (no prior-year close, no
        //    sibling close, no period.Activate()). None-division years and
        //    sub-period activations skip the guard entirely (FR-G3/G4).
        if (period.ParentPeriodId is null && period.Division != AcademicYearDivision.None)
        {
            var subPeriods = await repository.GetSubPeriodsAsync(period.Id, cancellationToken);
            if (!subPeriods.Any(sp => sp.Status == PeriodStatus.Draft))
            {
                throw new PeriodGuardException(
                    $"Cannot activate {period.Division} academic year '{period.Name}': " +
                    "it has no Draft sub-period. Create and activate at least one " +
                    $"{period.Division} first.");
            }
        }

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
            var eligible = candidates
                .Where(sp => sp.Status != PeriodStatus.Completed && sp.Status != PeriodStatus.Archived)
                .ToArray();
            var toActivate = eligible
                .Where(sp => sp.IsWithinActivationWindow(today, defaultTolerance))
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
            else if (eligible.Length > 0)
            {
                // FR-W5: the cascade only activates sub-periods inside their own activation
                // window. Zero in-window candidates is a valid gap state (FR-H4) — skip and log.
                logger.LogInformation(
                    "Skipping FR-H4a auto-activation for academic year {Id}: {EligibleCount} eligible " +
                    "sub-period(s) exist but none is inside its activation window (gap state stays valid, FR-H4)",
                    period.Id, eligible.Length);
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
