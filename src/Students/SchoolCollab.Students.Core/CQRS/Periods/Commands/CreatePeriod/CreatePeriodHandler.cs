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
    ILogger<CreatePeriodHandler> logger) : ICommandHandler<CreatePeriod, Guid>
{
    public async Task<Guid> HandleAsync(CreatePeriod command, CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(CreatePeriod), typeof(Period));

        logger.LogDebug("Handling CreatePeriod {Name}", command.Name);

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

        var period = Period.Create(
            command.Name,
            command.StartDate,
            command.EndDate,
            command.Division,
            command.ParentPeriodId)
            .WithTenant(tenantProvider);

        await repository.AddAsync(period, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        period.ClearDomainEvents();

        logger.LogInformation("Period {Id} created with name {Name}", period.Id, period.Name);
        return period.Id;
    }
}
