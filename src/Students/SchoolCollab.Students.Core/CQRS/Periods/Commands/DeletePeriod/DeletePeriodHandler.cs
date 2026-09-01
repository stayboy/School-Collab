using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.DeletePeriod;

/// <summary>
/// Deletes a Draft period (period-draft-delete.md FR-D1). All pre-mutation checks run
/// first so a failure leaves zero partial deletions (NFR-D1): the Draft-only domain
/// guard (FR-D2), the all-Draft sub-period guard for a top-level year (FR-D3), and the
/// FR-D6 dangling-link housekeeping. The single <c>Remove(year)</c> relies on the
/// already-declared EF <c>OnDelete(DeleteBehavior.Cascade)</c> for sub-period rows —
/// no per-row removal is implemented here.
/// </summary>
public sealed class DeletePeriodHandler(
    IPeriodRepository repository,
    HybridCache cache,
    ILogger<DeletePeriodHandler> logger) : ICommandHandler<DeletePeriod>
{
    public async Task HandleAsync(DeletePeriod command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling DeletePeriod {Id}", command.Id);

        // FR-D5 / NFR-D2: the tenant query filter makes other tenants' and already-deleted
        // rows return null here -> 404 (AC-D5).
        var period = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new PeriodNotFoundException(command.Id);

        // FR-D2: Draft-only domain guard (AC-D1) — throws PeriodNotDeletableException (422)
        // before any reads/mutations.
        period.Delete();

        // FR-D3: a top-level year may only be deleted while every sub-period is Draft.
        // Aborts before any removal and names the blocking row (AC-D3). This load also
        // serves the client-cascade: the sub-periods are tracked, so the single
        // Remove(year) below cascades to them in the same SaveChanges.
        if (period.ParentPeriodId is null)
        {
            var subs = await repository.GetSubPeriodsAsync(command.Id, cancellationToken);
            var blocker = subs.FirstOrDefault(sp => sp.Status != PeriodStatus.Draft);
            if (blocker is not null)
            {
                throw new PeriodNotDeletableException(
                    $"Cannot delete academic year '{period.Name}': sub-period '{blocker.Name}' " +
                    $"is {blocker.Status} and is still in use. A year can only be deleted while " +
                    "every sub-period is Draft.");
            }
        }

        // FR-D6 (SHOULD): clear dangling NextPeriodId links on surviving Draft periods.
        // Loaded tracked, nulled, and persisted by the same SaveChanges below (NFR-D1).
        // Non-Draft links stay untouched (EC-2: historical records).
        foreach (var linked in await repository.GetDraftPeriodsLinkedToAsync(command.Id, cancellationToken))
        {
            linked.ClearNextPeriod();
        }

        try
        {
            await repository.DeleteAsync(period, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Period", period.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        period.ClearDomainEvents();

        logger.LogInformation("Period {Id} deleted", period.Id);
    }
}
