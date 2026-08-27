using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.AddMembership;

public sealed class AddMembershipHandler(
    IActivityGroupRepository groupRepository,
    IActivityGroupMembershipRepository membershipRepository,
    IStudentRepository studentRepository,
    IStudentEnrollmentRepository enrollmentRepository,
    IActivePeriodProvider activePeriodProvider,
    IPeriodRepository periodRepository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<AddMembershipHandler> logger) : ICommandHandler<AddMembership, Guid>
{
    public async Task<Guid> HandleAsync(AddMembership command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling AddMembership student={StudentId} group={GroupId}",
            command.StudentId, command.ActivityGroupId);

        // Rev. 2 FR-12: reject membership for an inactive group.
        var group = await groupRepository.GetAsync(command.ActivityGroupId, cancellationToken)
            ?? throw new ActivityGroupNotFoundException(command.ActivityGroupId);

        if (!group.IsActive)
            throw new InactiveGroupException(command.ActivityGroupId);

        // FR-11: reject membership for a student that is soft-deleted, belongs
        // to a different tenant, or does not exist. The global tenant filter
        // and soft-delete filter on GetAsync handle all three cases — a null
        // result means the student is not visible in this tenant context.
        var student = await studentRepository.GetAsync(command.StudentId, cancellationToken)
            ?? throw new StudentNotFoundException(command.StudentId);

        // Rev. 2 FR-40: grade-eligibility check — if the group declares an
        // eligible-grade set (non-empty), the student's active grade-for-period
        // must be in it. Empty set = any grade (AC-40).
        await EnsureGradeEligible(group, command.StudentId, cancellationToken);

        // ── Rev. 3/4 span & window validation (FR-42/43/46/47/52) ────────────────
        // Resolve the membership's period/window from the group's span.
        var (periodId, windowStart, windowEnd) = await ResolveSpanAsync(group, command, cancellationToken);
        var autoRenew = command.AutoRenew ?? group.AutoRenewDefault;

        // FR-10: duplicate-active prevention — at most one active membership
        // per (tenant, student, group). (Phase 8/10 adds per-(group, period)
        // scoping for period-aligned spans; here the split DB index covers it.)
        var existing = await membershipRepository.GetActiveAsync(
            command.StudentId, command.ActivityGroupId, cancellationToken);

        if (existing is not null)
            throw new DuplicateActiveMembershipException(command.StudentId, command.ActivityGroupId);

        // FR-13/FR-46: capacity — per (group, period) when a PeriodId is set,
        // per group overall when null (OpenEnded/DateRange).
        if (group.Capacity.HasValue)
        {
            var activeCount = await groupRepository.CountActiveMembersAsync(
                command.ActivityGroupId, periodId, cancellationToken);

            if (activeCount >= group.Capacity.Value)
                throw new GroupAtCapacityException(
                    command.ActivityGroupId, group.Capacity.Value, activeCount);
        }

        // FR-15: strict tenant entity — inherit the current tenant context.
        var membership = ActivityGroupMembership.Create(
            command.ActivityGroupId, command.StudentId, periodId, autoRenew,
            windowStart, windowEnd, command.JoinedOn)
            .WithTenant(tenantProvider);

        await membershipRepository.AddAsync(membership, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        membership.ClearDomainEvents();

        logger.LogInformation("Membership {Id} created: student={StudentId} group={GroupId}",
            membership.Id, command.StudentId, command.ActivityGroupId);

        return membership.Id;
    }

    private async Task EnsureGradeEligible(ActivityGroup group, Guid studentId, CancellationToken cancellationToken)
    {
        var eligible = await groupRepository.GetEligibleGradeIdsAsync(group.Id, cancellationToken);
        if (eligible.Length == 0)
            return; // empty set = any grade.

        // Resolve the student's active grade-for-period (the active AcademicYear).
        var active = await activePeriodProvider.GetActivePeriodAsync(cancellationToken);
        if (active is null)
            throw new GradeNotEligibleException(group.Id, Guid.Empty);

        var enrollment = await enrollmentRepository.GetActiveEnrollmentByStudentAndPeriodAsync(
            studentId, active.Id, cancellationToken);

        if (enrollment is null || !eligible.Contains(enrollment.GradeLevelId))
            throw new GradeNotEligibleException(group.Id, enrollment?.GradeLevelId ?? Guid.Empty);
    }

    private async Task<(Guid? PeriodId, DateOnly? WindowStart, DateOnly? WindowEnd)>
        ResolveSpanAsync(ActivityGroup group, AddMembership command, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        switch (group.Span)
        {
            case EnrollmentSpan.DateRange:
                // FR-47/52: DateRange membership is window-scoped (null PeriodId), tied to
                // the group's current window; reject if the window has closed.
                if (command.PeriodId is not null)
                    throw new EnrollmentSpanMismatchException(group.Id, nameof(EnrollmentSpan.DateRange),
                        $"A DateRange activity group membership must not carry a PeriodId.");
                if (group.EnrollmentEndDate is { } end && end < today)
                    throw new EnrollmentWindowClosedException(group.Id, end);
                return (null, group.EnrollmentStartDate, group.EnrollmentEndDate);

            case EnrollmentSpan.OpenEnded:
                // FR-44/48: continuous membership — null PeriodId and null window.
                if (command.PeriodId is not null)
                    throw new EnrollmentSpanMismatchException(group.Id, nameof(EnrollmentSpan.OpenEnded),
                        $"An OpenEnded activity group membership must not carry a PeriodId.");
                return (null, null, null);

            default:
                // WholeAcademicYear/Termly/Semester: period-aligned — resolve the
                // matching typed period of the active academic year (Rev. 3 FR-43).
                var requiredType = group.Span switch
                {
                    EnrollmentSpan.WholeAcademicYear => PeriodType.AcademicYear,
                    EnrollmentSpan.Termly => PeriodType.Term,
                    _ => PeriodType.Semester
                };

                var activeYear = await periodRepository.GetActiveAcademicYearAsync(cancellationToken: cancellationToken);
                if (activeYear is null)
                    throw new EnrollmentSpanMismatchException(group.Id, group.Span.ToString(),
                        $"No active academic year exists for a {group.Span} membership.");

                Guid resolvedId;
                if (command.PeriodId is { } requestedId)
                {
                    var period = await periodRepository.GetAsync(requestedId, cancellationToken)
                        ?? throw new PeriodNotFoundException(requestedId);
                    if (period.PeriodType != requiredType)
                        throw new EnrollmentSpanMismatchException(group.Id, group.Span.ToString(),
                            $"A {group.Span} membership requires a {requiredType} period.");
                    if (requiredType != PeriodType.AcademicYear && period.ParentPeriodId != activeYear.Id)
                        throw new EnrollmentSpanMismatchException(group.Id, group.Span.ToString(),
                            $"The {requiredType} period must belong to the active academic year.");
                    resolvedId = requestedId;
                }
                else if (requiredType == PeriodType.AcademicYear)
                {
                    resolvedId = activeYear.Id;
                }
                else
                {
                    var activeSubs = await periodRepository.GetActiveSubPeriodsAsync(
                        activeYear.Id, requiredType, cancellationToken: cancellationToken);
                    var sub = activeSubs.FirstOrDefault();
                    if (sub is null)
                        throw new EnrollmentSpanMismatchException(group.Id, group.Span.ToString(),
                            $"No active {requiredType} period exists in the active academic year for a {group.Span} membership.");
                    resolvedId = sub.Id;
                }

                return (resolvedId, null, null);
        }
    }
}