using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UpdateAssignmentCommand;

public sealed class UpdateAssignmentCommandHandler(
    IAssignmentRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    IOptions<AttachmentUploadOptions> uploadOptions,
    IActivityGroupLookup groupLookup,
    IAssignmentPolicyResolver assignmentPolicyResolver,
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
        AssignmentContentValidator.ValidateModules(command.ContentModules);
        AssignmentContentValidator.ValidateResources(command.Resources);
        AssignmentContentValidator.ValidateAttachments(command.Attachments, uploadOptions.Value);

        // D6/D10: the three policy-derived assignment terms are RE-snapshotted from the CURRENTLY
        // resolved effective policy for the assignment's policy-scope grade — never from the
        // request. The inbound target set (non-null = full replacement) is the set this save
        // persists, so the scope is derived from it; a null inbound set preserves the persisted
        // rows and their scope (the null-means-preserve contract SetTargets honours below).
        var policyGradeIds = command.Targets is { } inboundTargets
            ? inboundTargets
                .Where(t => t.Kind == TargetKindDto.GradeLevel && t.RefId.HasValue)
                .Select(t => t.RefId!.Value)
                .ToList()
            : AssignmentPolicyScope.GradeTargetIds(assignment);
        var policy = await assignmentPolicyResolver.ResolveAsync(
            AssignmentPolicyScope.DeriveGrade(policyGradeIds), cancellationToken);

        // D15 (owner, 2026-10-09): reject an impossible type/grading pair before the aggregate
        // is mutated (an Offline assignment is always Teacher Marked).
        AssignmentTypeGradingRules.EnsurePermitted(
            (AssignmentTypeDto)(int)command.AssignmentType,
            (GradingFormatDto)(int)command.GradingFormat);

        assignment.Update(
            command.Title,
            command.Description,
            command.AssignmentType,
            command.GradingFormat,
            command.TargetAudienceType,
            command.TopicId,
            command.DueDate,
            command.MaxScore,
            mandatoryReview: policy.MandatoryReview ?? command.MandatoryReview ?? true,
            command.AiPromptOverride,
            // An unset policy leaves the built-in retention floor (30).
            archiveGraceDays: policy.ArchiveGraceDays ?? 30,
            // WS-A3 (spec §3.3 + §7 Q4): pass/fail threshold + attempt
            // cap — named args preserve the existing call style.
            passScore: command.PassScore,
            maxAttempts: command.MaxAttempts,
            // WS-C1/D10: the signature requirement is policy-decided; OD5 stores Optional as true.
            requiresSignature: policy.SignatureRequirement != SignatureRequirementMode.Disabled,
            // WS-B2 (spec §3.4 line 70): optional per-difficulty counts.
            difficultyEasy: command.DifficultyEasyCount,
            difficultyMedium: command.DifficultyMediumCount,
            difficultyHard: command.DifficultyHardCount,
            // INS-1 (assignment-authoring-compartments §9): student-facing text.
            instructions: command.Instructions);

        // R2 (D-1/TGT-1, UX-21): the authored targeting rows. Non-null = full replacement,
        // null = preserve (the same contract as questions/attachments/modules). SetTargets owns
        // validation (TGT-13/TGT-2/D-2) plus the derived compat column; the D-8.1 archived-group
        // rejection applies only to ids that are NEW relative to the persisted set, so an
        // unchanged link to a since-archived group still re-saves.
        var inactiveGroupIds = await ResolveInactiveActivityGroupIdsAsync(command.Targets, assignment.Targets, cancellationToken);
        assignment.SetTargets(
            command.Targets?.Select(t => (Kind: (TargetKind)t.Kind, RefId: t.RefId)).ToList(),
            assignment.TenantId,
            inactiveGroupIds);

        // R4 (CP-10/D23): the picks follow the questions/attachments precedent — null preserves
        // the persisted set, a non-null (even empty) list is a full replacement. The gate is per
        // PAIR of arguments while SetContextPicks preserves the kind whose argument is null, so
        // "clear the strands and leave the lessons alone" is expressible; the whole call is
        // skipped when neither kind was supplied, which is the null-means-preserve case.
        if (command.ContextStrandIds is not null || command.ContextLessonIds is not null)
        {
            assignment.SetContextPicks(command.ContextStrandIds, command.ContextLessonIds);
        }

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
                // R3 (D4/P1-2): carry GenerationId across the re-mint. The rows removed just above
                // are gone for good, so this hop is the ONLY thing that keeps provenance from being
                // stripped by the author's first save of a generated question set.
                var question = assignment.AddQuestion(q.QuestionText, (Domain.QuestionType)q.QuestionType, i, q.ModelAnswer, q.GenerationId);
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
                // R3 (D4/P1-3): the extraction outcome rides this DTO exactly as StoragePath does —
                // AddAttachment mints a new row, so anything omitted here is wiped by the next save.
                assignment.AddAttachment(
                    attachment.FileName,
                    attachment.ContentType,
                    attachment.FileSize,
                    attachment.StoragePath,
                    (AttachmentExtractionStatus)attachment.ExtractionStatus,
                    attachment.ExtractedText,
                    attachment.ExtractedAt,
                    attachment.ExtractionError);
            }
        }

        // WS-A1: full-replacement semantics for content modules + resources
        // (mirrors the questions / attachments pattern above): snapshot
        // existing ids, remove each, then re-add inbound. When the inbound
        // collection is null we preserve the current children (manual edit
        // may touch only the assignment properties); a non-null but empty
        // collection clears the children. AddModule uses the aggregate's
        // running _modules.Count as the DisplayOrder index so the inbound
        // list lands in contiguous 0..n order (EC-7 analog).
        if (command.ContentModules is not null)
        {
            var existingModuleIds = assignment.Modules.Select(m => m.Id).ToList();
            foreach (var mid in existingModuleIds)
            {
                assignment.RemoveModule(mid);
            }

            foreach (var m in command.ContentModules)
            {
                assignment.AddModule(
                    (ModuleType)m.ModuleType,
                    m.Url,
                    m.Title,
                    m.StoragePath,
                    m.MinCompletionThresholdPercent,
                    m.IsRequired);
            }
        }

        if (command.Resources is not null)
        {
            var existingResourceIds = assignment.Resources.Select(r => r.Id).ToList();
            foreach (var rid in existingResourceIds)
            {
                assignment.RemoveResource(rid);
            }

            foreach (var r in command.Resources)
            {
                assignment.AddResource(
                    (ResourceKind)r.ResourceKind,
                    r.Url,
                    r.StoragePath,
                    r.DisplayName,
                    r.IncludedInGeneration);
            }
        }

        // DetectChanges is required so the change tracker picks up field-backed
        // mutations on the standalone child collections (Modules / Resources)
        // as well as the owned-type collections (Questions / Attachments)
        // before SaveChanges runs. The Configuration sets
        // UsePropertyAccessMode(PropertyAccessMode.Field) for these
        // navigations, and the InMemory provider (used in unit tests) does
        // not detect field-level list mutations automatically. PostgreSQL at
        // runtime uses change-tracking proxies and would also benefit from
        // an explicit DetectChanges after this kind of replacement pattern.
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

    /// <summary>D-8.1: resolves the activity-group ids in <paramref name="targets"/> that are
    /// NEW relative to <paramref name="persistedTargets"/> and that the FR-21 port does not
    /// report as active — the only ids <c>SetTargets</c> rejects. A persisted target pointing at
    /// a group archived after linking is deliberately not re-validated (the historical row must
    /// not be silently dropped); an id the port omits (unknown / other tenant) is left to the
    /// route-level FR-21 checks.</summary>
    private async Task<Guid[]> ResolveInactiveActivityGroupIdsAsync(
        IReadOnlyList<AssignmentTargetDto>? targets,
        IReadOnlyList<AssignmentTarget> persistedTargets,
        CancellationToken ct)
    {
        var persistedGroupIds = persistedTargets
            .Where(t => t.Kind == TargetKind.ActivityGroup && t.RefId.HasValue)
            .Select(t => t.RefId!.Value)
            .ToHashSet();

        var groupIds = (targets ?? [])
            .Where(t => t.Kind == TargetKindDto.ActivityGroup && t.RefId.HasValue)
            .Select(t => t.RefId!.Value)
            .Where(id => !persistedGroupIds.Contains(id))
            .Distinct()
            .ToArray();
        if (groupIds.Length == 0)
        {
            return [];
        }

        var groups = await groupLookup.GetByIdsAsync(groupIds, ct);
        return groups.Where(g => !g.IsActive).Select(g => g.Id).ToArray();
    }
}