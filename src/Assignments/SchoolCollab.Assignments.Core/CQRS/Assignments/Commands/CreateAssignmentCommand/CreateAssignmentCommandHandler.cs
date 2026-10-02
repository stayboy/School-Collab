using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Core.Auth;
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
            QuestionOptionDtoValidator.ValidateQuestions(command.Questions);
        }
        AssignmentContentValidator.ValidateModules(command.ContentModules);
        AssignmentContentValidator.ValidateResources(command.Resources);
        AssignmentContentValidator.ValidateAttachments(command.Attachments, uploadOptions.Value);

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
            createdByTeacherId: createdByTeacherId,
            mandatoryReview: command.MandatoryReview,
            assignmentNumber: assignmentNumber,
            aiPromptOverride: command.AiPromptOverride,
            archiveGraceDays: command.ArchiveGraceDays,
            // WS-A3 (spec §3.3 + §7 Q4): pass/fail threshold + attempt
            // cap — named args preserve the existing call style.
            passScore: command.PassScore,
            maxAttempts: command.MaxAttempts,
            // WS-C1 (spec §7 Q1): guardian-signature snapshot, thread-through.
            requiresSignature: command.RequiresSignature,
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