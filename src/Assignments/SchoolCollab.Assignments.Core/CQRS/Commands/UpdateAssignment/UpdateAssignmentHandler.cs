using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Messaging;

namespace SchoolCollab.Assignments.Core.Commands.UpdateAssignment;

public sealed class UpdateAssignmentHandler(
    IAssignmentRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<UpdateAssignmentHandler> logger) : ICommandHandler<UpdateAssignmentCommand>
{
    public async Task HandleAsync(UpdateAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling UpdateAssignment {Id}", command.Id);

        var assignment = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.Id);

        assignment.Update(
            command.Title,
            command.Description,
            command.AssignmentType,
            command.SubjectCodedValueId,
            command.GradeCodedValueId,
            command.DueDate,
            command.MaxScore);

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        foreach (var _ in assignment.DomainEvents.OfType<Domain.Events.AssignmentUpdatedEvent>())
        {
            await publisher.PublishAsync(
                new { assignment.Id, assignment.Title, assignment.UpdatedAt },
                cancellationToken);
        }

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} updated", assignment.Id);
    }
}