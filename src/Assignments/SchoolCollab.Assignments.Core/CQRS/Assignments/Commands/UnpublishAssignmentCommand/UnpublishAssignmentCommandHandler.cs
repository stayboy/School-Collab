using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UnpublishAssignmentCommand;

public sealed class UnpublishAssignmentCommandHandler(
    IAssignmentRepository repository,
    ISubmissionRepository submissionRepository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<UnpublishAssignmentCommandHandler> logger) : ICommandHandler<UnpublishAssignmentCommand>
{
    public async Task HandleAsync(UnpublishAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling UnpublishAssignment {Id}", command.Id);

        var assignment = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.Id);

        assignment.Unpublish();

        // §4: rebuild recipients + reset the gate on unpublish (submissions/versions retained).
        await submissionRepository.DeleteRecipientsForAssignmentAsync(command.Id, cancellationToken);
        foreach (var gate in await submissionRepository.ListGatesForAssignmentAsync(command.Id, cancellationToken))
        {
            gate.Reset();
            submissionRepository.Update(gate);
        }

        await repository.UpdateAsync(assignment, cancellationToken);
        await submissionRepository.SaveChangesAsync(cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        foreach (var _ in assignment.DomainEvents.OfType<Domain.Events.AssignmentUnpublishedEvent>())
        {
            await publisher.EnqueueAsync(
                new AssignmentUnpublishedIntegrationEvent(
                    assignment.Id,
                    assignment.Title,
                    assignment.UpdatedAt),
                cancellationToken);
        }

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} unpublished (recipients rebuilt, gate reset)", assignment.Id);
    }
}