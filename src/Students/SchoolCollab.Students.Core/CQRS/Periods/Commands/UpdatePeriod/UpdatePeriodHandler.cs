using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.UpdatePeriod;

public sealed class UpdatePeriodHandler(
    IPeriodRepository repository,
    HybridCache cache,
    IAcademicYearDivisionProvider divisionProvider,
    ILogger<UpdatePeriodHandler> logger) : ICommandHandler<UpdatePeriod>
{
    public async Task HandleAsync(UpdatePeriod command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling UpdatePeriod {Id}", command.Id);

        var period = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new PeriodNotFoundException(command.Id);

        // ── Period hierarchy (FR-H2): if this is a sub-period, its parent must be
        //    an existing AcademicYear period. The null/required shape is enforced
        //    by the entity.
        if (command.PeriodType != Domain.PeriodType.AcademicYear)
        {
            if (!command.ParentPeriodId.HasValue)
                throw new ArgumentException(
                    $"A {command.PeriodType} period requires a ParentPeriodId.", nameof(command.ParentPeriodId));

            var parent = await repository.GetAsync(command.ParentPeriodId.Value, cancellationToken)
                ?? throw new PeriodNotFoundException(command.ParentPeriodId.Value);

            if (parent.PeriodType != Domain.PeriodType.AcademicYear)
                throw new ArgumentException(
                    $"A {command.PeriodType} period's ParentPeriodId must reference an AcademicYear period.",
                    nameof(command.ParentPeriodId));

            // ── H4.1 (FR-H3): the sub-period's range must be contained within its
            //    parent year. Crossing a year boundary is also rejected here.
            if (command.StartDate < parent.StartDate || command.EndDate > parent.EndDate)
                throw new PeriodContainmentException(
                    command.PeriodType.ToString(), parent.Name, parent.StartDate, parent.EndDate);
        }

        // ── H3.4 (FR-H7): gate sub-period type on the tenant's academic-year
        //    division, mirroring CreatePeriodHandler. Catches the update-bypass
        //    where an AcademicYear is created then updated to Term/Semester.
        if (command.PeriodType != Domain.PeriodType.AcademicYear)
        {
            var division = await divisionProvider.GetDivisionAsync(cancellationToken);
            if (command.PeriodType == Domain.PeriodType.Term && division != "Terms")
                throw new PeriodFrameworkMismatchException(nameof(Domain.PeriodType.Term), division);
            if (command.PeriodType == Domain.PeriodType.Semester && division != "Semesters")
                throw new PeriodFrameworkMismatchException(nameof(Domain.PeriodType.Semester), division);
        }

        // ── No-overlap invariant (§5.6): reject if another period's range
        //    intersects the new [StartDate, EndDate].
        var overlapping = await repository.GetOverlappingPeriodsAsync(
            command.StartDate, command.EndDate, excludeId: command.Id,
            excludeParentId: command.PeriodType != Domain.PeriodType.AcademicYear
                ? command.ParentPeriodId
                : null,
            cancellationToken);
        if (overlapping.Length > 0)
        {
            throw new PeriodOverlapException(
                command.Id,
                $"Period '{command.Name}' ({command.StartDate:O}–{command.EndDate:O}) " +
                $"overlaps existing period '{overlapping[0].Name}' " +
                $"({overlapping[0].StartDate:O}–{overlapping[0].EndDate:O}).");
        }

        period.Update(command.Name, command.StartDate, command.EndDate, command.PeriodType, command.ParentPeriodId);

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