using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Messaging;

namespace SchoolCollab.Assignments.Core.Commands.CloseAssignment;

public sealed class CloseAssignmentHandler(
    IAssignmentRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<CloseAssignmentHandler> logger) : ICommandHandler<CloseAssignmentCommand>
{
    public async Task HandleAsync(CloseAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling CloseAssignment {Id}", command.Id);

        var assignment = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.Id);

        assignment.Close();
        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        foreach (var _ in assignment.DomainEvents.OfType<Domain.Events.AssignmentClosedEvent>())
        {
            await publisher.PublishAsync(
                new { assignment.Id, assignment.Title, assignment.UpdatedAt },
                cancellationToken);
        }

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} closed", assignment.Id);
    }
}