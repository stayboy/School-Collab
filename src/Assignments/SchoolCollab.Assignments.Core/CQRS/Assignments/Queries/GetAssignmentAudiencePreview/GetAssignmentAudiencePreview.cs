using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentAudiencePreview;

/// <summary>
/// The DRAFT audience read (spec §4.2 Q152, round plan P2b): the same rows the published read
/// returns, resolved from the assignment's persisted targets through the same resolver (which
/// supplies both the cohort and its names), the same
/// batched subscriber read and the same policy filter publish runs — with
/// <see cref="AudienceDeliveryStateDto.NotPublishingYet"/> where the published read would carry a
/// delivery state. <b>Nothing is persisted</b>: publish re-resolves, and the panel says so in words.
/// <para>It exists because the published read is log-backed and a draft has no log rows, while the
/// view page draws one table in both states (Q152). It is a page-load read, never the authoring
/// surface's 500 ms debounce — that one keeps the counts-only <c>recipient-preview</c>.</para>
/// </summary>
public sealed record GetAssignmentAudiencePreview(Guid AssignmentId) : IQuery<AssignmentAudienceDto>;

/// <summary>Handles <see cref="GetAssignmentAudiencePreview"/>.</summary>
/// <remarks>
/// A contact the publish-time filter would drop is still returned, as
/// <see cref="AudienceDeliveryStateDto.BlockedByPolicy"/>: the draft panel exists to answer "who will
/// be told", and an omitted contact answers it with silence. Nothing here writes — no recipient row,
/// no gate, no log entry.
/// <para>Student names ride the target resolver's read, exactly as in the published read, so a
/// student with no subscribed contact is still named; the subscriber read is the contact source
/// only.</para>
/// </remarks>
public sealed class GetAssignmentAudiencePreviewHandler(
    AssignmentsDbContext db,
    IAssignmentTargetResolver targetResolver,
    IContactResolver contactResolver,
    INotificationPolicyResolver policyResolver,
    IAssignmentPolicyResolver assignmentPolicyResolver,
    ILogger<GetAssignmentAudiencePreviewHandler> logger)
    : IQueryHandler<GetAssignmentAudiencePreview, AssignmentAudienceDto>
{
    /// <inheritdoc />
    public async Task<AssignmentAudienceDto> HandleAsync(
        GetAssignmentAudiencePreview query,
        CancellationToken cancellationToken = default)
    {
        var assignment = await db.Assignments
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == query.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(query.AssignmentId);

        var targets = assignment.Targets;
        if (targets.Count == 0)
        {
            return new AssignmentAudienceDto([], false, false);
        }

        TargetStudent[] students;
        try
        {
            students = await targetResolver.ResolveStudentsAsync(
                targets.Select(target => new TargetConstraint(target.Kind, target.RefId)).ToArray(),
                targets.Any(target => target.Kind == TargetKind.AllStudents),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Target resolution failed for assignment {AssignmentId}; the draft audience preview is unavailable",
                assignment.Id);
            return new AssignmentAudienceDto([], false, true);
        }

        if (students.Length == 0)
        {
            return new AssignmentAudienceDto([], false, false);
        }

        // The names ride the cohort read, so a student with no subscribed contact still carries
        // one; the subscriber read below is the CONTACT source only.
        var cohort = students.Select(student => student.Id).ToArray();

        var tenantId = db.CurrentTenantId;
        var gradeTargetIds = AssignmentPolicyScope.GradeTargetIds(assignment);
        var policyGradeId = AssignmentPolicyScope.DeriveGrade(gradeTargetIds);

        IReadOnlyList<SubscriberInfo> subscribers;
        try
        {
            subscribers = await contactResolver.ResolveSubscribersAsync(
                new ResolveSubscribersRequest(
                    tenantId,
                    SubscriptionScope.AllAssignments,
                    gradeTargetIds,
                    cohort,
                    assignment.TopicId),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Subscriber resolution failed for assignment {AssignmentId}; the draft audience preview is unavailable",
                assignment.Id);
            return new AssignmentAudienceDto([], false, true);
        }

        // The recipients are transient projections — they exist only to feed the one pure filter
        // publish itself runs, so "will notify" means exactly what publish would send.
        var projected = subscribers
            .Where(subscriber => subscriber.StudentId.HasValue)
            .Select(subscriber => AssignmentRecipient.Create(
                tenantId,
                Guid.Empty,
                subscriber.OwnerType,
                subscriber.OwnerId,
                subscriber.StudentId,
                subscriber.ContactId,
                subscriber.Channel,
                subscriber.Role,
                notifyOnBroadcast: true,
                subscriptionActive: true))
            .ToList();

        // The two policy reads are a remote hop like the two above: an outage there must degrade the
        // panel to its stated empty state, never 500 the endpoint the page loads with.
        EffectiveNotificationPolicy effectivePolicy;
        EffectiveAssignmentPolicy assignmentPolicy;
        try
        {
            effectivePolicy = await policyResolver.ResolveEffectiveAsync(tenantId, policyGradeId, cancellationToken);
            assignmentPolicy = await assignmentPolicyResolver.ResolveAsync(policyGradeId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Policy resolution failed for assignment {AssignmentId}; the draft audience preview is unavailable",
                assignment.Id);
            return new AssignmentAudienceDto([], false, true);
        }

        var reachable = NotificationRecipientFilter
            .Apply(projected, effectivePolicy, assignmentPolicy)
            .Select(recipient => (recipient.ContactId, recipient.WardStudentId))
            .ToHashSet();

        var ordered = students
            .OrderBy(student => student.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(student => student.Id)
            .Take(AudienceLimits.AudienceCap)
            .ToArray();

        var rows = ordered
            .Select(student => new AudienceStudentDto(
                student.Id,
                student.FullName,
                projected.Exists(recipient =>
                    recipient.WardStudentId == student.Id
                    && reachable.Contains((recipient.ContactId, recipient.WardStudentId))),
                projected
                    .Where(recipient => recipient.WardStudentId == student.Id)
                    .OrderBy(recipient => (int)recipient.Channel)
                    .ThenBy(recipient => (int)recipient.OwnerType)
                    .Select(recipient => new AudienceContactDto(
                        (ContactOwnerTypeDto)recipient.OwnerType,
                        (ContactChannelDto)recipient.Channel,
                        recipient.Role is { } role ? (GuardianRoleDto)role : null,
                        recipient.SubscriptionActive,
                        recipient.NotifyOnBroadcast,
                        reachable.Contains((recipient.ContactId, recipient.WardStudentId))
                            ? AudienceDeliveryStateDto.NotPublishingYet
                            : AudienceDeliveryStateDto.BlockedByPolicy,
                        SentAt: null,
                        OpenedAt: null,
                        FailureReason: null,
                        RetryScheduled: false,
                        TemplateId: null))
                    .ToList()))
            .ToList();

        return new AssignmentAudienceDto(rows, students.Length > AudienceLimits.AudienceCap, false);
    }
}
