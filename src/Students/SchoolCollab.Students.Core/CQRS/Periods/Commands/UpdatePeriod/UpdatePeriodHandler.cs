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

        // ── plan-drop-periodtype.md: a top-level year → sub-period flip must not
        //    orphan sub-periods. Guarded here (handler) before the entity Update, so
        //    repo rows are never mutated on rejection.
        if (period.ParentPeriodId is null && command.ParentPeriodId is not null)
        {
            var children = await repository.GetSubPeriodsAsync(period.Id, cancellationToken);
            if (children.Length > 0)
            {
                throw new PeriodFrameworkMismatchException(
                    $"Cannot change '{period.Name}' from an academic year to a sub-period: " +
                    $"{children.Length} sub-period(s) still exist. Remove them first.");
            }
        }

        // ── Period hierarchy (plan-drop-periodtype.md): if this is a sub-period, its
        //    parent must be an existing top-level academic year with the SAME
        //    division. The null/required shape is enforced by the entity.
        if (command.ParentPeriodId is { } parentId)
        {
            if (command.Division == AcademicYearDivision.None)
                throw new ArgumentException(
                    "A sub-period must have a Terms or Semesters division.", nameof(command.Division));

            var parent = await repository.GetAsync(parentId, cancellationToken)
                ?? throw new PeriodNotFoundException(parentId);

            if (parent.ParentPeriodId is not null)
                throw new ArgumentException(
                    "A sub-period's ParentPeriodId must reference a top-level academic year.",
                    nameof(command.ParentPeriodId));

            if (parent.Division != command.Division)
                throw new PeriodFrameworkMismatchException(
                    command.Division.ToString(), parent.Division.ToString());

            // ── H4.1 (FR-H3): the sub-period's range must be contained within its
            //    parent year. Crossing a year boundary is also rejected here.
            if (command.StartDate < parent.StartDate || command.EndDate > parent.EndDate)
                throw new PeriodContainmentException(
                    command.Division.ToString(), parent.Name, parent.StartDate, parent.EndDate);
        }
        else
        {
            // ── plan-drop-periodtype.md: changing a top-level year's division is
            //    rejected while non-completed sub-periods exist (the operator must
            //    complete/remove them first).
            if (command.Division != period.Division)
            {
                var subCount = await repository.GetNonCompletedSubPeriodCountAsync(period.Id, cancellationToken);
                if (subCount > 0)
                {
                    throw new PeriodFrameworkMismatchException(
                        $"Cannot change the division of academic year '{period.Name}' from " +
                        $"'{period.Division}' to '{command.Division}': {subCount} sub-period(s) still exist. " +
                        "Complete or remove them first.");
                }
            }
        }

        // ── No-overlap invariant (§5.6): reject if another period's range
        //    intersects the new [StartDate, EndDate]. When updating a top-level year,
        //    its own sub-periods are excluded (they are contained within the year by
        //    definition — FR-H3); when updating a sub-period, its parent year is
        //    excluded.
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

        period.Update(command.Name, command.StartDate, command.EndDate, command.Division, command.ParentPeriodId);

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
