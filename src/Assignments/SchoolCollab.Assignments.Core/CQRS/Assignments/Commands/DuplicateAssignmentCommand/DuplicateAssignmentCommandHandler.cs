using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.DuplicateAssignmentCommand;

/// <summary>Duplicates an existing assignment as a fresh Draft template copy
/// (WS-A4 / spec §3.1). The source is loaded with its children (the aggregate
/// config AutoIncludes questions+options, reviews, attachments, modules,
/// resources), cloned via <see cref="Assignment.Create"/> with a new
/// <c>ASSIGNMENT_CODE</c> and a <c>" (copy)"</c> title suffix, and its
/// questions/options (with re-pointed <c>CorrectOptionId</c>), attachments,
/// modules, and resources are copied through the aggregate's <c>Add*</c>
/// factories. Reviews, recipients, gates, submissions, activity-group links,
/// approval stamps, and publish stamps are never copied — the clone is a
/// fresh unapproved Draft. The create-handler tail is mirrored verbatim: the
/// <see cref="AssignmentCreatedIntegrationEvent"/> is enqueued, the
/// <c>assignments</c> cache tag is invalidated, and the domain events are
/// cleared before the clone id is returned.</summary>
public sealed class DuplicateAssignmentCommandHandler(
    IAssignmentRepository repository,
    IEntityCodeGenerator entityCodeGenerator,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<DuplicateAssignmentCommandHandler> logger) : ICommandHandler<DuplicateAssignmentCommand, Guid>
{
    public async Task<Guid> HandleAsync(DuplicateAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Duplicating assignment {SourceId}", command.SourceId);

        var source = await repository.GetAsync(command.SourceId, cancellationToken);
        if (source is null)
        {
            throw new AssignmentNotFoundException(command.SourceId);
        }

        // Spec §4.5: auto-generate a NEW assignment code for the copy (the
        // create-handler precedent) — the copy must not reuse the source's.
        var assignmentNumber = await entityCodeGenerator.GenerateAsync("ASSIGNMENT_CODE", cancellationToken);

        // Clone via the aggregate factory — the ONLY construction path. The
        // copy is a fresh Draft (Create's default); Publish/Schedule/etc. are
        // never called on it. Attribution is copied verbatim from the source
        // (the D-6 identity round fixes this and the create path together).
        var clone = Assignment.Create(
            source.Title + " (copy)",
            source.Description,
            source.AssignmentType,
            source.GradingFormat,
            source.TargetAudienceType,
            source.TopicId,
            source.DueDate,
            source.MaxScore,
            createdByTeacherId: source.CreatedByTeacherId,
            // D4 (this round): the clone COERCES rather than re-resolving. A duplicate copies the
            // source's terms instead of re-authoring them, and a legacy row may legally hold
            // RequiresSignature = true with MandatoryReview = false (the retired author checkboxes
            // allowed exactly that pair) — without the coercion the new Assignment.Create guard
            // would throw on duplicating such a row.
            mandatoryReview: source.MandatoryReview || source.RequiresSignature,
            assignmentNumber: assignmentNumber,
            aiPromptOverride: source.AiPromptOverride,
            archiveGraceDays: source.ArchiveGraceDays,
            passScore: source.PassScore,
            maxAttempts: source.MaxAttempts,
            requiresSignature: source.RequiresSignature,
            // WS-B2 (spec §3.4 line 70): difficulty mix cloned with the other
            // scalar fields (the unconfirmed questions draft blob is NOT copied).
            difficultyEasy: source.DifficultyEasyCount,
            difficultyMedium: source.DifficultyMediumCount,
            difficultyHard: source.DifficultyHardCount,
            // INS-1 (assignment-authoring-compartments §9): the student-facing text is
            // cloned with the other scalar fields (the questions draft blob is still NOT
            // copied).
            instructions: source.Instructions)
            .WithTenant(tenantProvider);

        // Children copied IN ORDER via the aggregate's Add* factories (the
        // only child entry points — the same wiring the create path uses).
        // EF collection order is not guaranteed, so questions and modules
        // are sorted by their persisted DisplayOrder key.

        // Questions: re-index DisplayOrder 0..n by sorted position (EC-7).
        var sortedQuestions = source.Questions.OrderBy(q => q.DisplayOrder).ToList();
        for (var i = 0; i < sortedQuestions.Count; i++)
        {
            var q = sortedQuestions[i];
            var newQ = clone.AddQuestion(q.QuestionText, q.QuestionType, i, q.ModelAnswer);
            // Options in loaded order; AddOption(isCorrect: true) re-points
            // CorrectOptionId to the NEW option's id (the create-path mapping).
            foreach (var opt in q.Options)
            {
                newQ.AddOption(opt.OptionText, opt.IsCorrect);
            }
        }

        foreach (var attachment in source.Attachments)
        {
            clone.AddAttachment(attachment.FileName, attachment.ContentType, attachment.FileSize, attachment.StoragePath);
        }

        // Modules: AddModule assigns the aggregate's running _modules.Count as
        // DisplayOrder, so sorted-position calls reproduce contiguous 0..n
        // (order, threshold, required preserved).
        foreach (var m in source.Modules.OrderBy(m => m.DisplayOrder))
        {
            clone.AddModule(m.ModuleType, m.Url, m.Title, m.StoragePath, m.MinCompletionThresholdPercent, m.IsRequired);
        }

        foreach (var r in source.Resources)
        {
            clone.AddResource(r.ResourceKind, r.Url, r.StoragePath, r.DisplayName, r.IncludedInGeneration);
        }

        // R2 (TGT-1 / D-1): the authored targeting rows are duplicated with the other
        // structural children — never the recipients. A source with no rows (the zero-target
        // legacy class, D-3) is left zero-target: an empty replacement would be rejected by
        // TGT-13, and the clone is re-authored before it can publish anyway. No archived-group
        // resolution is performed, so duplicating an assignment whose group was archived after
        // linking keeps its rows (the D-8.1 unchanged-id posture).
        if (source.Targets.Count > 0)
        {
            clone.SetTargets(
                source.Targets
                    .OrderBy(t => t.DisplayOrder)
                    .Select(t => (Kind: t.Kind, RefId: t.RefId))
                    .ToList(),
                clone.TenantId);
        }

        // R4 (CP-5/D23): a copy carries its context picks — the same unconditional copy the
        // modules/resources above are, and the only path by which "round-trip through
        // create/update/duplicate" holds for the picks. Both lists are already normalized
        // (Distinct, empty-not-null), so they pass straight through.
        clone.SetContextPicks(source.ContextStrandIds, source.ContextLessonIds);

        // The duplicate IS a new assignment creation on the wire — mirror the
        // create-handler tail verbatim: enqueue the integration event for each
        // AssignmentCreatedEvent (Create() already emits it), persist, invalidate
        // the assignments cache tag, clear the domain events, and log.
        foreach (var _ in clone.DomainEvents.OfType<Domain.Events.AssignmentCreatedEvent>())
        {
            await publisher.EnqueueAsync(
                new AssignmentCreatedIntegrationEvent(
                    clone.Id,
                    clone.Title,
                    clone.AssignmentNumber,
                    clone.CreatedAt),
                cancellationToken);
        }

        await repository.AddAsync(clone, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        clone.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} duplicated from source {SourceId} with number {AssignmentNumber}",
            clone.Id, command.SourceId, clone.AssignmentNumber);
        return clone.Id;
    }
}
