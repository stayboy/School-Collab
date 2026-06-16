using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Messaging;

namespace SchoolCollab.Assignments.Core.Commands.CreateAssignment;

public sealed class CreateAssignmentHandler(
    IAssignmentRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<CreateAssignmentHandler> logger) : ICommandHandler<CreateAssignmentCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling CreateAssignment {Title}", command.Title);

        var assignment = Assignment.Create(
            command.Title,
            command.Description,
            command.AssignmentType,
            command.SubjectCodedValueId,
            command.GradeCodedValueId,
            command.DueDate,
            command.MaxScore,
            createdByTeacherId: Guid.Empty); // TODO: wire up authenticated teacher ID

        await repository.AddAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        foreach (var _ in assignment.DomainEvents.OfType<Domain.Events.AssignmentCreatedEvent>())
        {
            await publisher.PublishAsync(
                new { assignment.Id, assignment.Title, assignment.CreatedAt },
                cancellationToken);
        }

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} created with title {Title}", assignment.Id, assignment.Title);
        return assignment.Id;
    }
}