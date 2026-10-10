using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Features;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;

public sealed class CreateAssignmentCommandHandler(
    IAssignmentRepository repository,
    IEntityCodeGenerator entityCodeGenerator,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ITenantProvider tenantProvider,
    IOptions<AttachmentUploadOptions> uploadOptions,
    ICurrentUser currentUser,
    ITeacherDirectory teacherDirectory,
    IFeatureFlagService featureFlags,
    IActivityGroupLookup groupLookup,
    IAssignmentPolicyResolver assignmentPolicyResolver,
    ILogger<CreateAssignmentCommandHandler> logger) : ICommandHandler<CreateAssignmentCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling CreateAssignment {Title}", command.Title);

        var tenantContext = tenantProvider.GetTenantContext();

        // ar-20 P1-6 server-authoritative attribution, keyed on AUTH MODE (not claim
        // absence): in real-auth (OIDC/bearer) a missing teacher_id is REJECTED — the wire
        // has no teacher field, so there is nothing to fall back to. In TestAuth/dev the
        // claim (if any) wins, else Guid.Empty (preserves today's posture). A present claim
        // is always validated against the teacher directory (UnknownTeacher on miss).
        var realAuth = !await featureFlags.IsEnabledAsync(FeatureFlagKeys.DisableOIDCAuth, cancellationToken);
        var teacherClaim = currentUser.TeacherId;
        Guid createdByTeacherId;
        if (teacherClaim.HasValue)
        {
            createdByTeacherId = teacherClaim.Value;
            var teacherExists = await teacherDirectory.ExistsAsync(createdByTeacherId, cancellationToken);
            if (!teacherExists)
            {
                throw new UnknownTeacherException(createdByTeacherId);
            }
        }
        else if (realAuth)
        {
            throw new MissingTeacherPrincipalException(nameof(CreateAssignmentCommand));
        }
        else
        {
            createdByTeacherId = Guid.Empty;
        }

        // Spec §4.5: auto-generate the assignment code before constructing the entity.
        var assignmentNumber = await entityCodeGenerator.GenerateAsync("ASSIGNMENT_CODE", cancellationToken);

        // Validate ALL inbound child collections BEFORE constructing the aggregate so a
        // FR-252 violation never leaves partial children on the domain (EC-7). The
        // domain helpers below are the single way questions/options/attachments/modules/
        // resources enter the aggregate (spec §3.3 / WS-A1).
        if (command.Questions is { Count: > 0 })
        {
            QuestionOptionDtoValidator.ValidateQuestions(
                command.Questions, (GradingFormatDto)(int)command.GradingFormat);
        }

        // QR-5 (§5.2/§5.6): the assignment's own instruction blocks, validated before the aggregate
        // exists — the same EC-7 posture as every other inbound child collection above.
        InstructionDtoValidator.ValidateAll(command.InstructionItems, "This assignment");
        AssignmentContentValidator.ValidateModules(command.ContentModules);
        AssignmentContentValidator.ValidateResources(command.Resources);
        AssignmentContentValidator.ValidateAttachments(command.Attachments, uploadOptions.Value);

        // D6/D10: the three policy-derived assignment terms are snapshotted from the CURRENTLY
        // RESOLVED effective policy for the assignment's policy-scope grade — never from the
        // request. The request keeps only the AUTHOR half of guardian review (OD1).
        var policy = await assignmentPolicyResolver.ResolveAsync(
            DerivePolicyGrade(command.Targets), cancellationToken);

        // D15 (owner, 2026-10-09): the assignment type defines the permitted grading formats —
        // an Offline assignment can never be auto-scored. Fail before the aggregate is built.
        AssignmentTypeGradingRules.EnsurePermitted(
            (AssignmentTypeDto)(int)command.AssignmentType,
            (GradingFormatDto)(int)command.GradingFormat);

        // Q5 (spec question-response-types §5.3): every response kind is media — video, audio, a
        // document, an image — so any question that defines kinds needs Teacher Marked grading.
        // Fail before the aggregate is built, beside the D15 rule above.
        QuestionResponseKindRules.EnsurePermitted(
            (GradingFormatDto)(int)command.GradingFormat,
            (command.Questions ?? []).SelectMany(q => q.ResponseKinds ?? []).ToList());

        // QR-5/§5.6: the instruction rows for the whole aggregate — the ASSIGNMENT's own first
        // (QuestionId null), then each question's, stamped with the id the aggregate mints below.
        var instructionItems = (command.InstructionItems ?? []).Select(item => (
            QuestionId: (Guid?)null,
            Kind: (InstructionKind)(int)item.Kind,
            item.Text,
            item.Url,
            item.FileName,
            item.ContentType,
            item.FileSize,
            item.StoragePath,
            item.Title)).ToList();

        var assignment = Assignment.Create(
            command.Title,
            command.Description,
            command.AssignmentType,
            command.GradingFormat,
            command.TargetAudienceType,
            command.TopicId,
            command.DueDate,
            command.MaxScore,
            createdByTeacherId: createdByTeacherId,
            mandatoryReview: policy.MandatoryReview ?? command.MandatoryReview ?? true,
            assignmentNumber: assignmentNumber,
            aiPromptOverride: command.AiPromptOverride,
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
            instructions: command.Instructions)
            .WithTenant(tenantProvider);

        // R2 (D-1/TGT-1): attach the authored targeting rows. SetTargets — not Create — owns
        // their validation (TGT-13/TGT-2/D-2/D-8.1) and the derived compat column, and the
        // child rows carry the tenant id that WithTenant has just stamped. Every group id here
        // is newly added (a create has no persisted targets), so each is resolved through the
        // FR-21/FR-22 port and any inactive one is rejected (D-8.1).
        var inactiveGroupIds = await ResolveInactiveActivityGroupIdsAsync(command.Targets, cancellationToken);
        assignment.SetTargets(
            command.Targets?.Select(t => (Kind: (TargetKind)t.Kind, RefId: t.RefId)).ToList(),
            assignment.TenantId,
            inactiveGroupIds);

        // R4 (CP-5/D23): the picked strands & lessons. A create has no persisted set to
        // preserve, so both lists pass straight through (null and empty both mean "no picks").
        assignment.SetContextPicks(command.ContextStrandIds, command.ContextLessonIds);

        if (command.Questions is { Count: > 0 })
        {
            // Re-index DisplayOrder 0..n by list position (EC-7) — the payload's
            // DisplayOrder is informational; persistence is contiguous.
            for (var i = 0; i < command.Questions.Count; i++)
            {
                var q = command.Questions[i];
                var question = assignment.AddQuestion(
                    q.QuestionText,
                    (QuestionType)q.QuestionType,
                    i,
                    q.ModelAnswer,
                    q.GenerationId,
                    // D16/QR-2 (§5.1): the response kinds the validator has already required.
                    q.ResponseKinds?.Select(kind => (ResponseKind)(int)kind).ToList());
                if (q.Options is { Count: > 0 })
                {
                    foreach (var opt in q.Options)
                    {
                        question.AddOption(opt.OptionText, opt.IsCorrect);
                    }
                }

                // QR-5 (§5.2): this question's instruction blocks, stamped with the id the
                // aggregate just minted — the rows are re-minted on every save, so the id has to
                // be taken here (the same reason GenerationId rides the DTO).
                instructionItems.AddRange((q.Instructions ?? []).Select(item => (
                    QuestionId: (Guid?)question.Id,
                    Kind: (InstructionKind)(int)item.Kind,
                    item.Text,
                    item.Url,
                    item.FileName,
                    item.ContentType,
                    item.FileSize,
                    item.StoragePath,
                    item.Title)));
            }
        }

        if (instructionItems.Count > 0)
        {
            assignment.SetInstructionItems(instructionItems);
        }

        if (command.Attachments is { Count: > 0 })
        {
            foreach (var attachment in command.Attachments)
            {
                // R3 (D4/P1-3): the extraction outcome rides this DTO exactly as StoragePath does.
                // AddAttachment mints a fresh row on every save, so anything not carried here is
                // silently wiped by the author's next edit.
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

        // WS-A1: content modules (student-facing) + AI-generation resources.
        // AddModule uses the aggregate's running _modules.Count as the
        // DisplayOrder index, so calling it in inbound list order produces
        // DisplayOrder 0..n contiguously (EC-7 analog).
        if (command.ContentModules is { Count: > 0 })
        {
            for (var i = 0; i < command.ContentModules.Count; i++)
            {
                var m = command.ContentModules[i];
                assignment.AddModule(
                    (ModuleType)m.ModuleType,
                    m.Url,
                    m.Title,
                    m.StoragePath,
                    m.MinCompletionThresholdPercent,
                    m.IsRequired);
            }
        }
        if (command.Resources is { Count: > 0 })
        {
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

    /// <summary>D6/D7: the policy-scope grade for an inbound target set — the one-distinct-grade
    /// rule shared with the publish path (<see cref="AssignmentPolicyScope"/>), applied to the
    /// authored grade-target rows because the assignment does not exist yet. Null (no single grade
    /// target) resolves the tenant-global default.</summary>
    private static Guid? DerivePolicyGrade(IReadOnlyList<AssignmentTargetDto>? targets) =>
        targets is null
            ? null
            : AssignmentPolicyScope.DeriveGrade(
                targets
                    .Where(t => t.Kind == TargetKindDto.GradeLevel && t.RefId.HasValue)
                    .Select(t => t.RefId!.Value)
                    .ToList());

    /// <summary>D-8.1: resolves the activity-group ids in <paramref name="targets"/> that the
    /// FR-21 port does not report as active. A create has no persisted target rows, so every
    /// group id is newly added; an id the port omits (unknown / other tenant) is left to the
    /// route-level FR-21 checks and is not rejected here.</summary>
    private async Task<Guid[]> ResolveInactiveActivityGroupIdsAsync(
        IReadOnlyList<AssignmentTargetDto>? targets, CancellationToken ct)
    {
        var groupIds = (targets ?? [])
            .Where(t => t.Kind == TargetKindDto.ActivityGroup && t.RefId.HasValue)
            .Select(t => t.RefId!.Value)
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