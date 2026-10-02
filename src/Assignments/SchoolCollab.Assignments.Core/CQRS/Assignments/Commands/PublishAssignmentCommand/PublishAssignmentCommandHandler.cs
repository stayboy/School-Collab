using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;

public sealed class PublishAssignmentCommandHandler(
    IAssignmentRepository repository,
    ISubmissionRepository submissionRepository,
    IContactResolver contactResolver,
    ITopicAssignmentLookup topicAssignmentLookup,
    IAssignmentTargetResolver targetResolver,
    ITenantProvider tenantProvider,
    IAssignmentNotificationBroadcaster broadcaster,
    INotificationPolicyResolver policyResolver,
    IAssignmentPolicyResolver assignmentPolicyResolver,
    IFeatureFlagService featureFlags,
    IDeepLinkTokenMinter minter,
    HybridCache cache,
    ILogger<PublishAssignmentCommandHandler> logger) : ICommandHandler<PublishAssignmentCommand>
{
    public async Task HandleAsync(PublishAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling PublishAssignment {Id}", command.Id);

        var assignment = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.Id);

        // WS-A2 / spec §7 Q2 + D3 (documents/solution/assignment-policy-fields.md §5): the
        // approval requirement is the effective policy field OR'd with the
        // FEATURE:RequireAssignmentApproval flag for one release (the flag is retired in
        // Round B). When on, the row must carry ApprovalStatus.Approved — otherwise the
        // domain throws the typed AssignmentApprovalRequiredException which the API surface
        // maps to 400. The policy resolver is fail-open: a failed fetch resolves
        // RequiresApprovalBeforePublish = false, so a policy outage can never turn the gate
        // ON silently — only the flag still can.
        var assignmentPolicy = await assignmentPolicyResolver.ResolveAsync(assignment.GradeLevelId, cancellationToken);
        var approvalRequired = assignmentPolicy.RequiresApprovalBeforePublish
            || await featureFlags.IsEnabledAsync(FeatureFlagKeys.RequireAssignmentApproval, cancellationToken);

        assignment.Publish(approvalRequired);

        var tenantId = tenantProvider.GetTenantContext().TenantId;

        // WS-E1 (ar-14-deep-links): resolve the effective policy BEFORE persisting
        // recipients so each minted deep-link token can carry the resolved
        // LinkValidityDays (expiry = mint + LinkValidityDays ?? 7). The policy is
        // still applied to the broadcast audience after recipient resolution below.
        var effectivePolicy = await policyResolver.ResolveEffectiveAsync(tenantId, assignment.GradeLevelId, cancellationToken);
        var recipients = await ResolveRecipientsAndGatesAsync(
            assignment, tenantId, effectivePolicy.LinkValidityDays, command.ContactIds, cancellationToken);

        // Effective-policy resolution (notification-delivery-plan.md §3): drop blocked
        // channels, apply preferred-channel order, cap each guardian role at the
        // assignment policy's contact caps (D4) and cap the sendout at MaxNotifications.
        var broadcastRecipients = NotificationRecipientFilter.Apply(
            recipients, effectivePolicy, assignmentPolicy);
        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(assignment.Id, assignment.Title, assignment.PublishedAt ?? assignment.UpdatedAt, broadcastRecipients),
            cancellationToken);

        await repository.UpdateAsync(assignment, cancellationToken);
        await submissionRepository.SaveChangesAsync(cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} published (audience {AudienceType})",
            assignment.Id, assignment.TargetAudienceType);
    }

    /// <summary>
    /// Resolve subscribed contacts for the publish scope and persist one
    /// <see cref="AssignmentRecipient"/> per contact (deduplicated; optional
    /// contact selection subset per spec §8). When the assignment mandates
    /// guardian review, also ensure a <see cref="GuardianSubmissionGate"/> exists
    /// for every student who has a Primary guardian subscriber (spec §4.6 / §4.10).
    /// <para>R2 (TGT-9/TGT-10, D-6): the recipient cohort is the union resolved from the
    /// assignment's authored <see cref="AssignmentTarget"/> rows through
    /// <see cref="IAssignmentTargetResolver"/>; the resolved ids ride the existing
    /// <see cref="ResolveSubscribersRequest.StudentIds"/> seam, so contact / subscription /
    /// policy filtering is unchanged. Resolution is fail-closed: no targets (TGT-13), a
    /// resolver failure (the exception propagates) and an empty resolved set each refuse the
    /// publish.</para>
    /// </summary>
    private async Task<List<AssignmentRecipient>> ResolveRecipientsAndGatesAsync(
        Assignment assignment, Guid tenantId, int? linkValidityDays, IReadOnlyList<Guid>? selectedContactIds, CancellationToken cancellationToken)
    {
        // ── Rev. 6 FR-58: the assignment's subject must be assigned to its target
        //    audience for a period covering the effective date.
        var effectiveDate = DateOnly.FromDateTime(
            (assignment.DueDate ?? assignment.PublishedAt ?? DateTimeOffset.UtcNow).UtcDateTime);

        // TGT-13 (+ D-6(a)): the authored target rows are the only recipient source — an
        // assignment with none cannot publish. This mirrors (and replaces) the pre-R2 FR-23
        // empty-group-links refusal; the publish route maps InvalidOperationException to 400.
        var targets = assignment.Targets;
        if (targets.Count == 0)
        {
            throw new InvalidOperationException(
                $"Assignment '{assignment.Id}' has no targets; add at least one audience target before publishing.");
        }

        // D-6: the all-students leg is explicit — true iff the set carries an AllStudents row.
        var includeAllStudents = targets.Any(t => t.Kind == TargetKind.AllStudents);
        var constraints = targets
            .Select(t => new TargetConstraint(t.Kind, t.RefId))
            .ToArray();

        // TGT-10 / D-6(b): a resolver/transport failure propagates — publish is blocked and
        // never degrades to publishing to nobody or everybody.
        var resolvedStudentIds = await targetResolver.ResolveStudentIdsAsync(
            constraints, includeAllStudents, cancellationToken);

        // D-6(c): an empty resolved set is refused (fail-closed) — the deliberate mirror of
        // the fail-open policy resolver (assignment-policy.md §2).
        if (resolvedStudentIds.Length == 0)
        {
            throw new InvalidOperationException(
                $"Assignment '{assignment.Id}' targets resolve to no students; refusing to publish an empty sendout.");
        }

        // FR-58 topic gate (D-6): derived exclusively from the kinded target rows — grade
        // targets run the grade gate, activity-group targets the group gate (every group must
        // carry the subject). The AllStudents leg runs NO gate, and an authored primary grade
        // is a policy/authoring field rather than a delivery constraint, so it runs no grade
        // gate either; Stream/Student legs impose no gate.
        var groupIds = targets
            .Where(t => t.Kind == TargetKind.ActivityGroup && t.RefId.HasValue)
            .Select(t => t.RefId!.Value)
            .Distinct()
            .ToArray();
        if (groupIds.Length > 0
            && !await topicAssignmentLookup.IsTopicAssignedAsync(null, groupIds, assignment.TopicId, effectiveDate, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Assignment '{assignment.Id}' subject is not assigned to its linked activity group(s) for the effective period; assign the topic before publishing.");
        }

        foreach (var gradeId in targets
                     .Where(t => t.Kind == TargetKind.GradeLevel && t.RefId.HasValue)
                     .Select(t => t.RefId!.Value)
                     .Distinct())
        {
            if (!await topicAssignmentLookup.IsTopicAssignedAsync(gradeId, [], assignment.TopicId, effectiveDate, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Assignment '{assignment.Id}' subject is not assigned to the target grade for the effective period; assign the topic before publishing.");
            }
        }

        // TGT-9: the resolved target cohort rides the existing StudentIds seam. The primary
        // grade rides GradeLevelId so the grade's teacher-recipient leg survives (D-6's
        // documented widening for a group-only assignment carrying a primary grade).
        var request = new ResolveSubscribersRequest(
            tenantId, SubscriptionScope.AllAssignments, assignment.GradeLevelId, resolvedStudentIds);

        var subscribers = await contactResolver.ResolveSubscribersAsync(request, cancellationToken);

        var recipients = new List<AssignmentRecipient>();
        foreach (var s in subscribers)
        {
            // Optional contact selection (spec §8): publish to a subset of contacts.
            if (selectedContactIds is { Count: > 0 } && !selectedContactIds.Contains(s.ContactId))
                continue;

            var existing = await submissionRepository.GetRecipientAsync(assignment.Id, s.ContactId, cancellationToken);
            if (existing is null)
            {
                var recipient = AssignmentRecipient.Create(
                    tenantId, assignment.Id, s.OwnerType, s.OwnerId, s.StudentId,
                    s.ContactId, s.Channel, s.Role, notifyOnBroadcast: true, subscriptionActive: true);
                // WS-E1 (ar-14-deep-links): a new recipient ALWAYS mints a deep-link
                // token at publish, independent of the EnableDeepLinks flag.
                var fresh = minter.Mint(recipient, linkValidityDays, DateTimeOffset.UtcNow);
                recipient.AttachDeepLink(fresh.Token, fresh.ExpiresAt);
                submissionRepository.Add(recipient);
                recipients.Add(recipient);
            }
            else
            {
                // WS-E1 (ar-14-deep-links): idempotent republish — reuse an unexpired
                // token; re-mint when absent or expired.
                if (string.IsNullOrWhiteSpace(existing.DeepLinkToken) ||
                    existing.DeepLinkExpiresAt is null ||
                    existing.DeepLinkExpiresAt <= DateTimeOffset.UtcNow)
                {
                    var refreshed = minter.Mint(existing, linkValidityDays, DateTimeOffset.UtcNow);
                    existing.AttachDeepLink(refreshed.Token, refreshed.ExpiresAt);
                }
                existing.MarkSubscribed(true);
                submissionRepository.Update(existing);
                recipients.Add(existing);
            }
        }

        if (!assignment.MandatoryReview)
            return recipients;

        var studentsWithPrimary = recipients
            .Where(r => r.Role == GuardianRole.Primary && r.WardStudentId.HasValue)
            .Select(r => r.WardStudentId!.Value)
            .Distinct()
            .ToArray();

        foreach (var studentId in studentsWithPrimary)
        {
            var gate = await submissionRepository.GetGateByAssignmentStudentAsync(assignment.Id, studentId, cancellationToken);
            if (gate is null)
                submissionRepository.Add(GuardianSubmissionGate.Create(tenantId, assignment.Id, studentId));
        }

        return recipients;
    }
}