using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;

public sealed class CreatePeriodHandler(
    IPeriodRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    IAcademicYearDivisionProvider divisionProvider,
    ILogger<CreatePeriodHandler> logger) : ICommandHandler<CreatePeriod, Guid>
{
    public async Task<Guid> HandleAsync(CreatePeriod command, CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(CreatePeriod), typeof(Period));

        logger.LogDebug("Handling CreatePeriod {Name}", command.Name);

        // ── Period hierarchy (FR-H2): a sub-period's parent must be an existing
        //    AcademicYear period. The null/required shape is enforced by the entity.
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

        // ── H3.4 (FR-H7): gate sub-period creation on the tenant's academic-year
        //    division. Term requires 'Terms'; Semester requires 'Semesters'.
        if (command.PeriodType != Domain.PeriodType.AcademicYear)
        {
            var division = await divisionProvider.GetDivisionAsync(cancellationToken);
            if (command.PeriodType == Domain.PeriodType.Term && division != "Terms")
                throw new PeriodFrameworkMismatchException(nameof(Domain.PeriodType.Term), division);
            if (command.PeriodType == Domain.PeriodType.Semester && division != "Semesters")
                throw new PeriodFrameworkMismatchException(nameof(Domain.PeriodType.Semester), division);
        }

        // ── No-overlap invariant (§5.6): reject if another period's range
        //    intersects [StartDate, EndDate]. Checked in the handler (which can
        //    query the repository) rather than the domain entity.
        var overlapping = await repository.GetOverlappingPeriodsAsync(
            command.StartDate, command.EndDate, excludeId: null,
            excludeParentId: command.PeriodType != Domain.PeriodType.AcademicYear
                ? command.ParentPeriodId
                : null,
            cancellationToken);
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
            command.PeriodType,
            command.ParentPeriodId)
            .WithTenant(tenantProvider);

        await repository.AddAsync(period, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        period.ClearDomainEvents();

        logger.LogInformation("Period {Id} created with name {Name}", period.Id, period.Name);
        return period.Id;
    }
}