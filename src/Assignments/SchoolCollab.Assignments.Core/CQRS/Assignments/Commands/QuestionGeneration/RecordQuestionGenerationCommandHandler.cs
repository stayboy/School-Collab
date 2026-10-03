namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionGeneration;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.CQRS;

/// <summary>
/// R3 (D4/P1-2) — handler for <see cref="RecordQuestionGenerationCommand"/>. Validates the inbound
/// shape, resolves the assignment's tenant, appends <b>exactly one</b> header row and returns its id.
/// <para>The write happens here — not on save — because the question sync re-mints every question row
/// (<c>RemoveQuestion</c> → <c>AddQuestion</c>), so a save-time writer would have no stable identity to
/// link to and the first save would silently strip provenance.</para>
/// <para>The provider/model are <b>relayed</b>, never resolved: the Assignments host is forbidden from
/// registering <c>ChatModelResolver</c> or any config-based AI resolver
/// (<c>.github/copilot/rules/ai-services.md</c>), and the author's request never carries a model (D6).</para>
/// </summary>
public sealed class RecordQuestionGenerationCommandHandler(
    AssignmentsDbContext db,
    IAssignmentQuestionGenerationRepository generations,
    ILogger<RecordQuestionGenerationCommandHandler> logger)
    : ICommandHandler<RecordQuestionGenerationCommand, Guid>
{
    /// <summary>Mirrors <c>AssignmentQuestionGenerationService.MaxQuestionCount</c> — the AI host
    /// rejects anything larger, so a header that claims more could never be true.</summary>
    public const int MaxQuestionCount = 30;

    /// <summary>The <c>Types</c> column cap; three comma-separated names fit comfortably.</summary>
    public const int MaxTypesLength = 100;

    public async Task<Guid> HandleAsync(
        RecordQuestionGenerationCommand command,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Recording question generation for assignment {AssignmentId}", command.AssignmentId);

        if (command.QuestionCount < 1 || command.QuestionCount > MaxQuestionCount)
        {
            throw new AssignmentContentValidationException(
                $"Question count must be between 1 and {MaxQuestionCount}.");
        }

        // P1-1/D6: the model is the AI host's own resolution. A missing one means the caller skipped
        // or mangled the hand-off — refuse rather than write a header that records nothing.
        if (string.IsNullOrWhiteSpace(command.Model))
        {
            throw new AssignmentContentValidationException(
                "A resolved model is required to record a question generation.");
        }

        var types = FormatTypes(command.Types);
        if (types is { Length: > MaxTypesLength })
        {
            throw new AssignmentContentValidationException(
                $"The question type list must be {MaxTypesLength} characters or fewer.");
        }

        // Tenant-scoped read: a cross-tenant id reads as absent, exactly like GetAssignmentByIdQuery.
        // Only the tenant id is needed, so the aggregate (with its AutoInclude'd children) is never loaded.
        var tenantId = await db.Assignments
            .AsNoTracking()
            .Where(a => a.Id == command.AssignmentId)
            .Select(a => (Guid?)a.TenantId)
            .SingleOrDefaultAsync(cancellationToken);

        if (tenantId is null)
        {
            throw new AssignmentNotFoundException(command.AssignmentId);
        }

        var generation = AssignmentQuestionGeneration.Create(
            tenantId.Value,
            command.AssignmentId,
            command.QuestionCount,
            types,
            command.DifficultyEasyCount,
            command.DifficultyMediumCount,
            command.DifficultyHardCount,
            command.Provider,
            command.Model!);

        generations.Add(generation);
        await generations.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Recorded question generation {GenerationId} for assignment {AssignmentId} via {Provider}/{Model}",
            generation.Id, command.AssignmentId, generation.Provider, generation.Model);

        return generation.Id;
    }

    /// <summary>The requested type mix as comma-separated wire enum names; null when the author left
    /// "all types" selected (the AI host's balanced default, FR-221).</summary>
    internal static string? FormatTypes(IReadOnlyList<QuestionTypeDto>? types)
        => types is { Count: > 0 } ? string.Join(',', types.Select(t => t.ToString())) : null;
}
