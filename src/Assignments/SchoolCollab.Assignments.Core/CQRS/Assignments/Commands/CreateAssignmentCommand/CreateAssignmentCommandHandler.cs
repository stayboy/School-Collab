using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;

public sealed class CreateAssignmentCommandHandler(
    IAssignmentRepository repository,
    IEntityCodeGenerator entityCodeGenerator,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<CreateAssignmentCommandHandler> logger) : ICommandHandler<CreateAssignmentCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling CreateAssignment {Title}", command.Title);

        var tenantContext = tenantProvider.GetTenantContext();

        // Spec §4.5: auto-generate the assignment code before constructing the entity.
        var assignmentNumber = await entityCodeGenerator.GenerateAsync("ASSIGNMENT_CODE", cancellationToken);

        var assignment = Assignment.Create(
            command.Title,
            command.Description,
            command.AssignmentType,
            command.GradingFormat,
            command.TargetAudienceType,
            command.TopicId,
            command.GradeLevelId,
            command.DueDate,
            command.MaxScore,
            createdByTeacherId: Guid.Empty, // TODO: wire up authenticated teacher ID
            mandatoryReview: command.MandatoryReview,
            assignmentNumber: assignmentNumber)
            .WithTenant(tenantProvider);

        foreach (var _ in assignment.DomainEvents.OfType<Domain.Events.AssignmentCreatedEvent>())
        {
            await publisher.EnqueueAsync(
                new AssignmentCreatedIntegrationEvent(
                    assignment.Id,
                    assignment.Title,
                    assignment.AssignmentNumber,
                    assignment.CreatedAt),
                cancellationToken);
        }

        await repository.AddAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);


        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} created with number {AssignmentNumber} for tenant {TenantId}",
            assignment.Id, assignment.AssignmentNumber, tenantContext.TenantId);
        return assignment.Id;
    }
}