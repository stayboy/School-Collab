using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.RolloverActivityGroup;

/// <summary>
/// Rev. 5 rollover (spec activity-group-enrollment.md FR-50/54): at a bounded
/// window's end, exit each active member (ExitedOn = trigger date), re-enrol
/// <c>AutoRenew = true</c> members into the next window (if one is defined),
/// then advance the group's window. <see cref="EnrollmentSpan.OpenEnded"/> is a
/// no-op (no window end). Used by both the admin-forced command and the
/// scheduled background job.
/// </summary>
public sealed class RolloverActivityGroupHandler(
    IActivityGroupRepository groupRepository,
    IActivityGroupMembershipRepository membershipRepository,
    ITenantProvider tenantProvider,
    IPeriodRepository periodRepository,
    HybridCache cache,
    ILogger<RolloverActivityGroupHandler> logger) : ICommandHandler<RolloverActivityGroup>
{
    public async Task HandleAsync(RolloverActivityGroup command, CancellationToken cancellationToken = default)
    {
        var group = await groupRepository.GetAsync(command.ActivityGroupId, cancellationToken)
            ?? throw new ActivityGroupNotFoundException(command.ActivityGroupId);

        // FR-44/48/54: OpenEnded has no window end and no rollover — no-op.
        if (group.Span == EnrollmentSpan.OpenEnded)
            return;

        var trigger = command.TriggerDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var members = await membershipRepository.ListActiveAsync(group.Id, cancellationToken);

        // FR-50: exit every active member at the window end (ExitedOn = trigger).
        // Exit-before-create preserves the FR-10 active-uniqueness invariant: the
        // exits are committed in a single SaveChanges before any new active
        // membership is inserted (SaveChanges #1).
        foreach (var m in members)
            m.Exit(trigger);
        if (members.Length > 0)
            await membershipRepository.SaveChangesAsync(cancellationToken);

        // FR-50/51: re-enrol AutoRenew members into the next window. For DateRange
        // that is the group's advance slot; for period-aligned spans it is the
        // active typed period of the active academic year.
        Guid? nextPeriodId = null;
        DateOnly? nextStart = null;
        DateOnly? nextEnd = null;

        if (group.Span == EnrollmentSpan.DateRange)
        {
            if (group.NextEnrollmentStartDate.HasValue && group.NextEnrollmentEndDate.HasValue)
            {
                nextStart = group.NextEnrollmentStartDate;
                nextEnd = group.NextEnrollmentEndDate;
            }
        }
        else
        {
            var requiredDivision = group.Span switch
            {
                EnrollmentSpan.WholeAcademicYear => (AcademicYearDivision?)null,
                EnrollmentSpan.Termly => AcademicYearDivision.Terms,
                _ => AcademicYearDivision.Semesters
            };
            var activeYear = await periodRepository.GetActiveAcademicYearAsync(
                cancellationToken: cancellationToken);
            if (group.Span == EnrollmentSpan.WholeAcademicYear)
                nextPeriodId = activeYear?.Id;
            else if (activeYear is not null)
            {
                var subs = await periodRepository.GetActiveSubPeriodsAsync(
                    activeYear.Id, requiredDivision, cancellationToken: cancellationToken);
                nextPeriodId = subs.FirstOrDefault()?.Id;
            }
        }

        if (nextPeriodId.HasValue || nextStart.HasValue)
        {
            var renewals = new List<ActivityGroupMembership>();
            foreach (var m in members.Where(m => m.AutoRenew))
            {
                renewals.Add(ActivityGroupMembership.Create(
                    group.Id, m.StudentId, periodId: nextPeriodId, autoRenew: true,
                    windowStartDate: nextStart, windowEndDate: nextEnd,
                    joinedOn: nextStart ?? trigger)
                    .WithTenant(tenantProvider));
            }

            if (group.Span == EnrollmentSpan.DateRange && nextStart.HasValue)
                group.AdvanceToNextWindow();

            // SaveChanges #2: commit the renewals (and any DateRange window advance)
            // in a single round-trip after the exits have already been persisted.
            await membershipRepository.AddRangeAsync(renewals, cancellationToken);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        foreach (var m in members)
            m.ClearDomainEvents();
        group.ClearDomainEvents();

        logger.LogInformation("ActivityGroup {Id} rolled over on {Trigger}; {Count} members",
            group.Id, trigger, members.Length);
    }
}