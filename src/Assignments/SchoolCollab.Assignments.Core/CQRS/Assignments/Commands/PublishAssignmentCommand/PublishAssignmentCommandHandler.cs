using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;

public sealed class PublishAssignmentCommandHandler(
    IAssignmentRepository repository,
    ISubmissionRepository submissionRepository,
    IContactResolver contactResolver,
    ITenantProvider tenantProvider,
    IIntegrationEventPublisher publisher,
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
        await ResolveRecipientsAndGatesAsync(assignment, tenantId, cancellationToken);

        await repository.UpdateAsync(assignment, cancellationToken);
        await submissionRepository.SaveChangesAsync(cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        foreach (var _ in assignment.DomainEvents.OfType<Domain.Events.AssignmentPublishedEvent>())
        {
            await publisher.EnqueueAsync(
                new AssignmentPublishedIntegrationEvent(
                    assignment.Id,
                    assignment.Title,
                    assignment.UpdatedAt),
                cancellationToken);
        }

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} published (recipients resolved for grade {GradeLevelId})",
            assignment.Id, assignment.GradeLevelId);
    }

    /// <summary>
    /// Resolve subscribed contacts for the publish scope and persist one
    /// <see cref="AssignmentRecipient"/> per contact. When the assignment
    /// mandates guardian review, also ensure a <see cref="GuardianSubmissionGate"/>
    /// exists for every student who has a Primary guardian subscriber (spec §4.6 / §4.10).
    /// </summary>
    private async Task ResolveRecipientsAndGatesAsync(Assignment assignment, Guid tenantId, CancellationToken cancellationToken)
    {
        var subscribers = await contactResolver.ResolveSubscribersAsync(
            new ResolveSubscribersRequest(tenantId, SubscriptionScope.AllAssignments, assignment.GradeLevelId),
            cancellationToken);

        foreach (var s in subscribers)
        {
            var existing = await submissionRepository.GetRecipientAsync(assignment.Id, s.ContactId, cancellationToken);
            if (existing is null)
            {
                var recipient = AssignmentRecipient.Create(
                    tenantId, assignment.Id, s.OwnerType, s.OwnerId, s.StudentId,
                    s.ContactId, s.Channel, s.Role, notifyOnBroadcast: true, subscriptionActive: true);
                submissionRepository.Add(recipient);
            }
            else
            {
                existing.MarkSubscribed(true);
                submissionRepository.Update(existing);
            }
        }

        if (!assignment.MandatoryReview)
            return;

        var studentsWithPrimary = subscribers
            .Where(s => s.Role == GuardianRole.Primary && s.StudentId.HasValue)
            .Select(s => s.StudentId!.Value)
            .Distinct()
            .ToArray();

        foreach (var studentId in studentsWithPrimary)
        {
            var gate = await submissionRepository.GetGateByAssignmentStudentAsync(assignment.Id, studentId, cancellationToken);
            if (gate is null)
            {
                submissionRepository.Add(GuardianSubmissionGate.Create(tenantId, assignment.Id, studentId));
            }
        }
    }
}
