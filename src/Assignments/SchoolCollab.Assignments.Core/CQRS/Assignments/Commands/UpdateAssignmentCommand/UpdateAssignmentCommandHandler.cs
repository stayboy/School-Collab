using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UpdateAssignmentCommand;

public sealed class UpdateAssignmentCommandHandler(
    IAssignmentRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<UpdateAssignmentCommandHandler> logger) : ICommandHandler<UpdateAssignmentCommand>
{
    public async Task HandleAsync(UpdateAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling UpdateAssignment {Id}", command.Id);

        var assignment = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.Id);

        // Validate ALL inbound child collections BEFORE mutating the aggregate so a
        // FR-252 violation never leaves the aggregate with a partial replacement.
        if (command.Questions is { Count: > 0 })
        {
            QuestionOptionDtoValidator.ValidateQuestions(command.Questions);
        }

        assignment.Update(
            command.Title,
            command.Description,
            command.AssignmentType,
            command.GradingFormat,
            command.TargetAudienceType,
            command.TopicId,
            command.GradeLevelId,
            command.DueDate,
            command.MaxScore,
            command.MandatoryReview,
            command.AiPromptOverride);

        // Full-replacement semantics for questions + attachments (decision b):
        // snapshot existing child ids, remove each, then re-add inbound. Re-index
        // DisplayOrder 0..n by inbound list position (EC-7). When the inbound
        // collection is null we preserve the current children (manual edit may
        // touch only the assignment properties); a non-null but empty collection
        // clears the children.
        if (command.Questions is not null)
        {
            var existingQuestionIds = assignment.Questions.Select(q => q.Id).ToList();
            foreach (var qid in existingQuestionIds)
            {
                assignment.RemoveQuestion(qid);
            }

            for (var i = 0; i < command.Questions.Count; i++)
            {
                var q = command.Questions[i];
                var question = assignment.AddQuestion(q.QuestionText, (Domain.QuestionType)q.QuestionType, i, q.ModelAnswer);
                if (q.Options is { Count: > 0 })
                {
                    foreach (var opt in q.Options)
                    {
                        question.AddOption(opt.OptionText, opt.IsCorrect);
                    }
                }
            }
        }

        if (command.Attachments is not null)
        {
            var existingAttachmentIds = assignment.Attachments.Select(a => a.Id).ToList();
            foreach (var aid in existingAttachmentIds)
            {
                assignment.RemoveAttachment(aid);
            }

            foreach (var attachment in command.Attachments)
            {
                assignment.AddAttachment(attachment.FileName, attachment.ContentType, attachment.FileSize, attachment.StoragePath);
            }
        }

        // DetectChanges is required so the change tracker picks up field-backed
        // mutations on owned-type collections (Questions / Attachments) before
        // SaveChanges runs. The Configuration sets
        // UsePropertyAccessMode(PropertyAccessMode.Field) for these navigations,
        // and the InMemory provider (used in unit tests) does not detect
        // field-level list mutations automatically. PostgreSQL at runtime uses
        // change-tracking proxies and would also benefit from an explicit
        // DetectChanges after this kind of replacement pattern.
        repository.DetectChanges();

        foreach (var _ in assignment.DomainEvents.OfType<Domain.Events.AssignmentUpdatedEvent>())
        {
            await publisher.EnqueueAsync(
                new AssignmentUpdatedIntegrationEvent(
                    assignment.Id,
                    assignment.Title,
                    assignment.UpdatedAt),
                cancellationToken);
        }

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);


        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} updated", assignment.Id);
    }
}