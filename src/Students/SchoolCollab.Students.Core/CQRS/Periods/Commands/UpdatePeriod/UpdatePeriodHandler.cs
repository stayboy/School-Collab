using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.UpdatePeriod;

public sealed class UpdatePeriodHandler(
    IPeriodRepository repository,
    HybridCache cache,
    ILogger<UpdatePeriodHandler> logger) : ICommandHandler<UpdatePeriod>
{
    public async Task HandleAsync(UpdatePeriod command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling UpdatePeriod {Id}", command.Id);

        var period = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new PeriodNotFoundException(command.Id);

        // ── Identity cannot change (period-edit-parity-deactivate.md FR-E1): Division
        //    is immutable, so a top-level year can never become a sub-period and a
        //    sub-period (created as a Term/Semester) can never become a top-level year.
        if (command.ParentPeriodId is { } parentId)
        {
            // A sub-period's parent must be an existing top-level academic year of the
            // SAME (immutable) division.
            if (period.Division == AcademicYearDivision.None)
                throw new PeriodFrameworkMismatchException(
                    period.Division.ToString(), "Terms/Semesters");

            // A top-level year acquiring a parent must not orphan its sub-periods.
            if (period.ParentPeriodId is null)
            {
                var children = await repository.GetSubPeriodsAsync(period.Id, cancellationToken);
                if (children.Length > 0)
                {
                    throw new PeriodFrameworkMismatchException(
                        $"Cannot change '{period.Name}' from an academic year to a sub-period: " +
                        $"{children.Length} sub-period(s) still exist. Remove them first.");
                }
            }

            var parent = await repository.GetAsync(parentId, cancellationToken)
                ?? throw new PeriodNotFoundException(parentId);

            if (parent.ParentPeriodId is not null)
                throw new ArgumentException(
                    "A sub-period's ParentPeriodId must reference a top-level academic year.",
                    nameof(command.ParentPeriodId));

            if (parent.Division != period.Division)
                throw new PeriodFrameworkMismatchException(
                    period.Division.ToString(), parent.Division.ToString());

            // ── FR-H3: the sub-period's range must be contained within its parent year.
            //    Crossing a year boundary is also rejected here.
            if (command.StartDate < parent.StartDate || command.EndDate > parent.EndDate)
                throw new PeriodContainmentException(
                    period.Division.ToString(), parent.Name, parent.StartDate, parent.EndDate);
        }
        else
        {
            // A sub-period cannot be promoted to a top-level year by clearing its parent
            // (its Term/Semester division is fixed at creation).
            if (period.ParentPeriodId is not null)
                throw new PeriodFrameworkMismatchException(
                    $"Cannot change '{period.Name}' from a sub-period to an academic year " +
                    $"because its {period.Division} division is fixed at creation.");
        }

        // ── No-overlap invariant (§5.6): reject if another non-Deactivated period's range
        //    intersects the new range. Deactivated periods no longer block (FR-X3). When
        //    updating a top-level year its own sub-periods are excluded (contained by
        //    definition); when updating a sub-period its parent year is excluded.
        var overlapping = await repository.GetOverlappingPeriodsAsync(
            command.StartDate, command.EndDate, excludeId: command.Id,
            excludeParentId: command.ParentPeriodId,
            excludeSubPeriodsOfParentId: command.ParentPeriodId is null ? command.Id : null,
            cancellationToken);
        if (overlapping.Length > 0)
        {
            throw new PeriodOverlapException(
                command.Id,
                $"Period '{command.Name}' ({command.StartDate:O}–{command.EndDate:O}) " +
                $"overlaps existing period '{overlapping[0].Name}' " +
                $"({overlapping[0].StartDate:O}–{overlapping[0].EndDate:O}).");
        }

        period.Update(command.Name, command.StartDate, command.EndDate, command.ParentPeriodId);

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

        logger.LogInformation("Period {Id} updated", period.Id);
    }
}
