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
        await repository.UpdateAsync(assignment, cancellationToken);
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

        logger.LogInformation("Assignment {Id} unpublished", assignment.Id);
    }
}