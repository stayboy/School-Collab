using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Assignments.Contracts;
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

        // Validate ALL inbound child collections BEFORE constructing the aggregate so a
        // FR-252 violation never leaves partial children on the domain (EC-7). The
        // domain helpers below are the single way questions/options/attachments enter
        // the aggregate (spec §3.3).
        if (command.Questions is { Count: > 0 })
        {
            QuestionOptionDtoValidator.ValidateQuestions(command.Questions);
        }

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
            assignmentNumber: assignmentNumber,
            aiPromptOverride: command.AiPromptOverride)
            .WithTenant(tenantProvider);

        if (command.Questions is { Count: > 0 })
        {
            // Re-index DisplayOrder 0..n by list position (EC-7) — the payload's
            // DisplayOrder is informational; persistence is contiguous.
            for (var i = 0; i < command.Questions.Count; i++)
            {
                var q = command.Questions[i];
                var question = assignment.AddQuestion(q.QuestionText, (QuestionType)q.QuestionType, i, q.ModelAnswer);
                if (q.Options is { Count: > 0 })
                {
                    foreach (var opt in q.Options)
                    {
                        question.AddOption(opt.OptionText, opt.IsCorrect);
                    }
                }
            }
        }

        if (command.Attachments is { Count: > 0 })
        {
            foreach (var attachment in command.Attachments)
            {
                assignment.AddAttachment(attachment.FileName, attachment.ContentType, attachment.FileSize, attachment.StoragePath);
            }
        }

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