using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Notifications;
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
    INotificationTemplateResolver templateResolver,
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
        // Round drop-primary-grade: the policy-scope grade is DERIVED from the assignment's grade
        // targets (one distinct grade target ⇒ that grade, otherwise the tenant default) — the same
        // single rule the read handlers, the sweep reads and the preview use.
        var assignmentPolicy = await assignmentPolicyResolver.ResolveAsync(
            AssignmentPolicyScope.DeriveGrade(assignment), cancellationToken);
        var approvalRequired = assignmentPolicy.RequiresApprovalBeforePublish
            || await featureFlags.IsEnabledAsync(FeatureFlagKeys.RequireAssignmentApproval, cancellationToken);

        var tenantId = tenantProvider.GetTenantContext().TenantId;

        // §3.7 (WS4): validate + stamp the author's template selection BEFORE the publish state
        // change, so a rejected explicit stamp (400) leaves the assignment exactly as it was —
        // neither published nor carrying a partial stamp.
        await ApplyTemplateSelectionAsync(assignment, command, tenantId, cancellationToken);

        assignment.Publish(approvalRequired);

        // WS-E1 (ar-14-deep-links): resolve the effective policy BEFORE persisting
        // recipients so each minted deep-link token can carry the resolved
        // LinkValidityDays (expiry = mint + LinkValidityDays ?? 7). The policy is
        // still applied to the broadcast audience after recipient resolution below.
        var effectivePolicy = await policyResolver.ResolveEffectiveAsync(
            tenantId, AssignmentPolicyScope.DeriveGrade(assignment), cancellationToken);
        var recipients = await ResolveRecipientsAndGatesAsync(
            assignment, tenantId, effectivePolicy.LinkValidityDays, command.ContactIds, cancellationToken);

        // Effective-policy resolution (notification-delivery-plan.md §3): drop blocked
        // channels, apply preferred-channel order, cap each guardian role at the
        // assignment policy's contact caps (D4) and cap the sendout at MaxNotifications.
        // Round b2a (§3.8): the publisher's per-sendout medium restriction is applied INSIDE
        // this filter — after the ContactIds subset filter above and before the caps — and a
        // blocked channel still wins (the request narrows, never widens).
        var broadcastRecipients = NotificationRecipientFilter.Apply(
            recipients, effectivePolicy, assignmentPolicy, command.Channels);
        // Round b2c (§3.7/Q15): the broadcaster has no DbContext, so the selection inputs ride the
        // context — the assignment's live stamps (validated above) and its evaluation dimensions,
        // both projected from the entity already in hand (zero extra reads).
        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(
                assignment.Id,
                assignment.Title,
                assignment.PublishedAt ?? assignment.UpdatedAt,
                assignment.Description,
                assignment.DueDate,
                assignment.Instructions,
                broadcastRecipients,
                AssignmentTemplateContext.FromAssignment(assignment),
                assignment.PublishEmailTemplateId,
                assignment.PublishSmsTemplateId),
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

        // TGT-9: the resolved target cohort rides the existing StudentIds seam. The assignment's
        // distinct grade-target ids ride GradeLevelIds so each targeted grade's teacher-recipient
        // leg survives (round drop-primary-grade: the authored grade targets are the only grade
        // source — D-6's documented widening, now per target rather than per authored primary grade).
        var request = new ResolveSubscribersRequest(
            tenantId, SubscriptionScope.AllAssignments,
            AssignmentPolicyScope.GradeTargetIds(assignment), resolvedStudentIds);

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
                // §3.4: enrichment names ride the in-process recipient into the broadcaster's
                // render; empty when the Students response did not carry them.
                recipient.AttachDisplayNames(s.ContactName, s.StudentName);
                // WS-E1 (ar-14-deep-links): a new recipient ALWAYS mints a deep-link
                // token at publish, independent of the EnableDeepLinks flag. Round b2a §3.6:
                // an SMS/WhatsApp recipient mints its short-link code in the same call.
                await AttachDeepLinkAsync(recipient, s.Channel, linkValidityDays, cancellationToken);
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
                    await AttachDeepLinkAsync(existing, s.Channel, linkValidityDays, cancellationToken);
                }
                // Round b2a §3.6/Q64: an SMS/WhatsApp row written before short links existed keeps
                // its unexpired token and gains only the missing code — the same ensure-mint the
                // E3 sweeps perform at render time.
                else if (CarriesShortLink(s.Channel) && string.IsNullOrWhiteSpace(existing.ShortLinkCode))
                {
                    existing.EnsureShortLinkCode(await MintShortLinkCodeAsync(cancellationToken));
                }
                existing.MarkSubscribed(true);
                submissionRepository.Update(existing);
                existing.AttachDisplayNames(s.ContactName, s.StudentName);
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

    /// <summary>
    /// §3.7 selection lifecycle (rounds notification-templates-b2a/b2c). A request stamp is the
    /// author's explicit choice and must name one of this tenant's live Publish <b>candidates</b> for
    /// that channel — anything else is a <see cref="NotificationTemplateSelectionInvalidException"/> the
    /// route maps to 400, never a silent fallback. A null request stamp means <b>reuse</b>: the stored
    /// stamp survives untouched.
    /// <para>An unreadable Settings read (outage, timeout) accepts the stamp as-is: §3.3's fail-open
    /// outranks the 400 — a template outage must never block a publish — so the stamp is only
    /// rejected when the live candidates were actually read and none matches. A stale <b>stored</b>
    /// stamp is never validated here for the same reason (a transient outage must not erase the
    /// author's choice); the render path resolves the candidates independently, so a deleted stamp
    /// degrades to evaluation (or the built-in default) and the persisted
    /// <c>NotificationLog.TemplateId</c> records what actually rendered (§3.9).</para>
    /// </summary>
    private async Task ApplyTemplateSelectionAsync(
        Assignment assignment, PublishAssignmentCommand command, Guid tenantId, CancellationToken cancellationToken)
    {
        var emailId = command.PublishEmailTemplateId is { } requestedEmail
            ? await ValidateRequestedStampAsync(NotificationChannel.Email, requestedEmail, tenantId, cancellationToken)
            : assignment.PublishEmailTemplateId;
        var smsId = command.PublishSmsTemplateId is { } requestedSms
            ? await ValidateRequestedStampAsync(NotificationChannel.SMS, requestedSms, tenantId, cancellationToken)
            : assignment.PublishSmsTemplateId;

        if (emailId != assignment.PublishEmailTemplateId || smsId != assignment.PublishSmsTemplateId)
        {
            assignment.StampPublishTemplates(emailId, smsId);
        }
    }

    /// <summary>Validates one explicit request stamp against the channel's live candidates (see
    /// <see cref="ApplyTemplateSelectionAsync"/>). Three-way result:
    /// <list type="bullet">
    /// <item><description><see cref="NotificationTemplateSlotState.NotSet"/>: no live candidate is
    /// configured for this channel, so the stamp cannot be validated and a
    /// <see cref="NotificationTemplateSelectionInvalidException"/> is thrown (400).</description></item>
    /// <item><description><see cref="NotificationTemplateSlotState.Unreadable"/>: the Settings read
    /// failed (outage, timeout, or unparseable response). §3.3 fail-open outranks validation,
    /// so the stamp is accepted and persisted as-is and a warning is logged.</description></item>
    /// <item><description><see cref="NotificationTemplateSlotState.Found"/>: the stamp must name one of
    /// the live candidates, otherwise a
    /// <see cref="NotificationTemplateSelectionInvalidException"/> is thrown (400). <b>Existence only</b> —
    /// applicability is the dialog's pre-select guidance, never a 400 gate, so a live-but-not-applicable
    /// stamp is accepted (§3.7).</description></item>
    /// </list>
    /// Returns the stamp so the caller keeps it.</summary>
    private async Task<Guid> ValidateRequestedStampAsync(
        NotificationChannel channel, Guid templateId, Guid tenantId, CancellationToken cancellationToken)
    {
        var read = await templateResolver.ReadCandidatesAsync(
            NotificationKind.Publish, channel, tenantId, cancellationToken);

        switch (read.State)
        {
            case NotificationTemplateSlotState.NotSet:
                throw new NotificationTemplateSelectionInvalidException(
                    $"The selected {channel} template '{templateId}' is not one of this tenant's live Publish "
                    + "templates for that channel. Reload the publish options and choose again.");

            case NotificationTemplateSlotState.Unreadable:
                logger.LogWarning(
                    "Skipping {Channel} Publish template validation for '{TemplateId}' because the Settings "
                    + "template read failed; the stamp will be persisted as-is",
                    channel, templateId);
                return templateId;

            case NotificationTemplateSlotState.Found:
                if (!read.Candidates.Any(c => c.TemplateId == templateId))
                {
                    throw new NotificationTemplateSelectionInvalidException(
                        $"The selected {channel} template '{templateId}' is not one of this tenant's live Publish "
                        + "templates for that channel. Reload the publish options and choose again.");
                }
                return templateId;

            default:
                throw new NotificationTemplateSelectionInvalidException(
                    $"The selected {channel} template '{templateId}' could not be validated.");
        }
    }

    /// <summary>
    /// Stamps the deep link for one recipient (round b2a §3.6): the token, its expiry and — for the
    /// SMS/WhatsApp channels only — the short-link code, in ONE call, so a plain-text recipient can
    /// never hold a token without a code or a code without a token. Email keeps the full
    /// <c>/deeplink/{token}</c>. A republish re-mint reuses an existing code: the code resolves to
    /// this row (its validity is the row's expiry), so only a missing one is minted.
    /// </summary>
    private async Task AttachDeepLinkAsync(
        AssignmentRecipient recipient, ContactChannel channel, int? linkValidityDays, CancellationToken cancellationToken)
    {
        var minted = minter.Mint(recipient, linkValidityDays, DateTimeOffset.UtcNow);
        var shortLinkCode = CarriesShortLink(channel)
            ? recipient.ShortLinkCode ?? await MintShortLinkCodeAsync(cancellationToken)
            : null;
        recipient.AttachDeepLink(minted.Token, minted.ExpiresAt, shortLinkCode);
    }

    /// <summary>Mints a globally unused short-link code (§3.6, Q61) through the one shared
    /// generator/validator (the sweeps' ensure-mint uses the same loop).</summary>
    private Task<string> MintShortLinkCodeAsync(CancellationToken cancellationToken) =>
        ShortLinkCodes.MintUniqueAsync(submissionRepository.ShortLinkCodeExistsAsync, cancellationToken);

    /// <summary>Whether this channel's messages carry a short link — SMS and WhatsApp do, email keeps
    /// the full deep link (§3.2/§3.6).</summary>
    private static bool CarriesShortLink(ContactChannel channel) =>
        channel is ContactChannel.SMS or ContactChannel.WhatsApp;
}