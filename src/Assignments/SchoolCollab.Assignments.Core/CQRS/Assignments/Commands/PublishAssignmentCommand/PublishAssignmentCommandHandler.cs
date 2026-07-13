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
    ITenantProvider tenantProvider,
    IAssignmentNotificationBroadcaster broadcaster,
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

        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(assignment.Id, assignment.Title, assignment.PublishedAt ?? assignment.UpdatedAt, recipients),
            cancellationToken);

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} published (recipients resolved for grade {GradeLevelId})",
            assignment.Id, assignment.GradeLevelId);
    }

    /// <summary>
    /// Resolve subscribed contacts for the publish scope and persist one
    /// <see cref="AssignmentRecipient"/> per contact (deduplicated; optional
    /// contact selection subset per spec §8). When the assignment mandates
    /// guardian review, also ensure a <see cref="GuardianSubmissionGate"/> exists
    /// for every student who has a Primary guardian subscriber (spec §4.6 / §4.10).
    /// </summary>
    private async Task<List<AssignmentRecipient>> ResolveRecipientsAndGatesAsync(
        Assignment assignment, Guid tenantId, IReadOnlyList<Guid>? selectedContactIds, CancellationToken cancellationToken)
    {
        var subscribers = await contactResolver.ResolveSubscribersAsync(
            new ResolveSubscribersRequest(tenantId, SubscriptionScope.AllAssignments, assignment.GradeLevelId),
            cancellationToken);

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