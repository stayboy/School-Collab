using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.CreateActivityGroup;

public sealed class CreateActivityGroupHandler(
    IActivityGroupRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    IActivePeriodProvider activePeriodProvider,
    ILogger<CreateActivityGroupHandler> logger) : ICommandHandler<CreateActivityGroup, Guid>
{
    public async Task<Guid> HandleAsync(CreateActivityGroup command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(CreateActivityGroup), typeof(ActivityGroup));

        logger.LogDebug("Handling CreateActivityGroup {Name}", command.Name);

        // ── Rev. 3 FR-45: span/framework compatibility. Termly requires a terms
        //    framework; Semester requires semesters. Others are framework-agnostic.
        //    The division is read from the ACTIVE AcademicYear's Period.Division
        //    (Rev. 2 — no cross-context provider). No active year ⇒ fail-closed
        //    (same semantics as the old fail-open-to-None).
        if (command.Span is EnrollmentSpan.Termly or EnrollmentSpan.Semester)
        {
            var activeYear = await activePeriodProvider.GetActiveAcademicYearAsync(cancellationToken);
            var division = activeYear?.Division ?? nameof(AcademicYearDivision.None);
            var required = command.Span == EnrollmentSpan.Termly ? nameof(AcademicYearDivision.Terms) : nameof(AcademicYearDivision.Semesters);
            if (division != required)
                throw new EnrollmentSpanIncompatibleException(command.Span.ToString(), required);
        }

        var group = ActivityGroup.Create(
            command.Name, command.Description, command.Category, command.Capacity,
            command.Span, command.EnrollmentStartDate, command.EnrollmentEndDate,
            command.AutoRenewDefault)
            .WithTenant(tenantProvider);

        await repository.AddAsync(group, cancellationToken);

        // Rev. 2 FR-39/40: persist the eligible-grade set (empty = any grade).
        if (command.EligibleGradeIds is { Length: > 0 })
            await repository.SetEligibleGradesAsync(group.Id, command.EligibleGradeIds, cancellationToken);

        await cache.RemoveByTagAsync("students", cancellationToken);

        group.ClearDomainEvents();

        logger.LogInformation("ActivityGroup {Id} created with name {Name}", group.Id, group.Name);
        return group.Id;
    }
}