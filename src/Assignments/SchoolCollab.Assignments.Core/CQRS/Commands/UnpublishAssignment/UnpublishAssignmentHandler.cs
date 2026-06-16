using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Messaging;

namespace SchoolCollab.Assignments.Core.Commands.UnpublishAssignment;

public sealed class UnpublishAssignmentHandler(
    IAssignmentRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<UnpublishAssignmentHandler> logger) : ICommandHandler<UnpublishAssignmentCommand>
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
            await publisher.PublishAsync(
                new { assignment.Id, assignment.Title, assignment.UpdatedAt },
                cancellationToken);
        }

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} unpublished", assignment.Id);
    }
}