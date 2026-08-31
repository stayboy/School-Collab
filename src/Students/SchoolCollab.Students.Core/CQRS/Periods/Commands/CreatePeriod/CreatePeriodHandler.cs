using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;

public sealed class CreatePeriodHandler(
    IPeriodRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<CreatePeriodHandler> logger) : ICommandHandler<CreatePeriod, CreatePeriodResult>
{
    public async Task<CreatePeriodResult> HandleAsync(CreatePeriod command, CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(CreatePeriod), typeof(Period));

        logger.LogDebug("Handling CreatePeriod {Name}", command.Name);

        // ── FR-C1: sub-period definitions are only valid on a top-level
        //    Terms/Semesters academic year. A sub-period create (parent set) or a
        //    None-division year with a sub-period list is rejected (→ 400).
        var hasSubPeriods = command.SubPeriods is { Count: > 0 };
        if (hasSubPeriods && (command.ParentPeriodId is not null || command.Division == AcademicYearDivision.None))
        {
            throw new ArgumentException(
                "Sub-period definitions are only allowed when creating a top-level " +
                "Terms/Semesters academic year.", nameof(command.SubPeriods));
        }

        // ── Period hierarchy (plan-drop-periodtype.md): a sub-period's parent must
        //    be an existing top-level academic year with the SAME division. The
        //    null/required shape is enforced by the entity.
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

        // ── No-overlap invariant (§5.6): reject if another period's range
        //    intersects [StartDate, EndDate]. Checked in the handler (which can
        //    query the repository) rather than the domain entity.
        var overlapping = await repository.GetOverlappingPeriodsAsync(
            command.StartDate, command.EndDate, excludeId: null,
            excludeParentId: command.ParentPeriodId,
            cancellationToken: cancellationToken);
        if (overlapping.Length > 0)
        {
            throw new PeriodOverlapException(
                $"Period '{command.Name}' ({command.StartDate:O}–{command.EndDate:O}) " +
                $"overlaps existing period '{overlapping[0].Name}' " +
                $"({overlapping[0].StartDate:O}–{overlapping[0].EndDate:O}).");
        }

        // ── FR-C2: validate every sub-period definition BEFORE any persistence —
        //    a violation rejects the whole request (zero rows). Definitions are
        //    not yet rows, so sibling overlap is checked in-memory.
        if (hasSubPeriods)
        {
            foreach (var sub in command.SubPeriods!)
            {
                if (sub.EndDate < sub.StartDate)
                    throw new ArgumentException(
                        $"Sub-period '{sub.Name}' end date must be on or after its start date.",
                        nameof(sub.EndDate));

                if (sub.StartDate < command.StartDate || sub.EndDate > command.EndDate)
                    throw new PeriodContainmentException(
                        command.Division.ToString(), command.Name, command.StartDate, command.EndDate);
            }

            var defs = command.SubPeriods!;
            for (var i = 0; i < defs.Count; i++)
            {
                for (var j = i + 1; j < defs.Count; j++)
                {
                    var a = defs[i];
                    var b = defs[j];
                    if (a.StartDate <= b.EndDate && a.EndDate >= b.StartDate)
                    {
                        throw new PeriodOverlapException(
                            $"Sub-period '{a.Name}' ({a.StartDate:O}–{a.EndDate:O}) " +
                            $"overlaps sibling sub-period '{b.Name}' " +
                            $"({b.StartDate:O}–{b.EndDate:O}).");
                    }
                }
            }
        }

        // ── FR-C3: build the year + all sub-periods (all Draft) and persist the
        //    object graph in ONE unit of work — a failure at any point leaves zero
        //    rows. No auto-activation on create.
        var year = Period.Create(
            command.Name,
            command.StartDate,
            command.EndDate,
            command.Division,
            command.ParentPeriodId)
            .WithTenant(tenantProvider);

        var subPeriods = new List<Period>();
        if (hasSubPeriods)
        {
            foreach (var sub in command.SubPeriods!)
            {
                subPeriods.Add(Period.Create(
                    sub.Name,
                    sub.StartDate,
                    sub.EndDate,
                    command.Division,
                    parentPeriodId: year.Id)
                    .WithTenant(tenantProvider));
            }
        }

        var all = new List<Period> { year };
        all.AddRange(subPeriods);
        await repository.AddRangeAsync(all, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        foreach (var p in all)
            p.ClearDomainEvents();

        logger.LogInformation(
            "Period {Id} created with name {Name} and {SubCount} sub-period(s)",
            year.Id, year.Name, subPeriods.Count);
        return new CreatePeriodResult(year.Id, subPeriods.Select(p => p.Id).ToArray());
    }
}
