using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CloseAssignmentCommand;

public sealed class CloseAssignmentCommandHandler(
    IAssignmentRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<CloseAssignmentCommandHandler> logger) : ICommandHandler<CloseAssignmentCommand>
{
    public async Task HandleAsync(CloseAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling CloseAssignment {Id}", command.Id);

        var assignment = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.Id);

        assignment.Close();
        foreach (var _ in assignment.DomainEvents.OfType<Domain.Events.AssignmentClosedEvent>())
        {
            await publisher.EnqueueAsync(
                new AssignmentClosedIntegrationEvent(
                    assignment.Id,
                    assignment.Title,
                    assignment.UpdatedAt),
                cancellationToken);
        }

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);


        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} closed", assignment.Id);
    }
}