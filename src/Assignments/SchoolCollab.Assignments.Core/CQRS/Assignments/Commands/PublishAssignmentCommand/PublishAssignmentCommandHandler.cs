using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;

public sealed class PublishAssignmentCommandHandler(
    IAssignmentRepository repository,
    ISubmissionRepository submissionRepository,
    IContactResolver contactResolver,
    IAssignmentActivityGroupRepository linkRepository,
    IActivityGroupLookup groupLookup,
    ITenantProvider tenantProvider,
    IAssignmentNotificationBroadcaster broadcaster,
    INotificationPolicyResolver policyResolver,
    HybridCache cache,
    ILogger<PublishAssignmentCommandHandler> logger) : ICommandHandler<PublishAssignmentCommand>
{
    public async Task HandleAsync(PublishAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling PublishAssignment {Id}", command.Id);

        var assignment = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.Id);

        assignment.Publish();

        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var recipients = await ResolveRecipientsAndGatesAsync(assignment, tenantId, command.ContactIds, cancellationToken);

        await repository.UpdateAsync(assignment, cancellationToken);
        await submissionRepository.SaveChangesAsync(cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        // Effective-policy resolution (notification-delivery-plan.md §3): drop blocked
        // channels, apply preferred-channel order, cap the sendout at MaxNotifications.
        // The persisted AssignmentRecipient rows are the full subscription set; the policy
        // only shapes the broadcast set so a later policy change can re-broadcast.
        var effectivePolicy = await policyResolver.ResolveEffectiveAsync(tenantId, assignment.GradeLevelId, cancellationToken);
        var broadcastRecipients = NotificationRecipientFilter.Apply(recipients, effectivePolicy);

        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(assignment.Id, assignment.Title, assignment.PublishedAt ?? assignment.UpdatedAt, broadcastRecipients),
            cancellationToken);

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
    /// For <see cref="TargetAudienceType.SelectedGroups"/> the cohort is the active
    /// members of the linked groups (FR-20), archived groups excluded (EC-4); a
    /// SelectedGroups assignment with zero links cannot be published (FR-23).
    /// </summary>
    private async Task<List<AssignmentRecipient>> ResolveRecipientsAndGatesAsync(
        Assignment assignment, Guid tenantId, IReadOnlyList<Guid>? selectedContactIds, CancellationToken cancellationToken)
    {
        ResolveSubscribersRequest request;
        if (assignment.TargetAudienceType == TargetAudienceType.SelectedGroups)
        {
            // FR-18 / FR-20: target the active members of the linked groups.
            var groupIds = await linkRepository.GetGroupIdsForAssignmentAsync(assignment.Id, cancellationToken);

            // FR-23 / EC-7: an assignment targeting groups must have at least one
            // linked group before it can be published.
            if (groupIds.Length == 0)
                throw new InvalidOperationException(
                    $"Assignment '{assignment.Id}' targets SelectedGroups but has no linked activity groups; link at least one group before publishing.");

            // EC-4: archived groups are excluded from recipient resolution.
            var memberIds = await groupLookup.GetActiveMemberIdsAsync(groupIds, cancellationToken);
            request = new ResolveSubscribersRequest(tenantId, SubscriptionScope.AllAssignments, StudentIds: memberIds);
        }
        else
        {
            // AllStudents / SelectedGrades keep the existing grade-level path.
            request = new ResolveSubscribersRequest(tenantId, SubscriptionScope.AllAssignments, assignment.GradeLevelId);
        }

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
                submissionRepository.Add(recipient);
                recipients.Add(recipient);
            }
            else
            {
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