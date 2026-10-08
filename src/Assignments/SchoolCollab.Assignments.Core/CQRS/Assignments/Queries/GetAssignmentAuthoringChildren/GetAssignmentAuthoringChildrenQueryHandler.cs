using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentAuthoringChildren;

/// <summary>
/// Projects one assignment's persisted questions (with options), attachments and
/// resources (assignment-authoring-compartments P1 rework). The Edit surface loads
/// this BEFORE it renders the question / resource editors, so adding one child can
/// never make an otherwise-empty collection non-null and destructively replace the
/// persisted set (the update handler full-replaces any non-null collection).
/// <para>Tenant-scoped by the context's global query filter — a cross-tenant id reads
/// as absent, exactly like <see cref="GetAssignmentByIdQuery"/>.</para>
/// </summary>
public sealed class GetAssignmentAuthoringChildrenQueryHandler(
    AssignmentsDbContext db,
    ILogger<GetAssignmentAuthoringChildrenQueryHandler> logger)
    : IQueryHandler<GetAssignmentAuthoringChildrenQuery, AssignmentAuthoringChildrenDto?>
{
    public async Task<AssignmentAuthoringChildrenDto?> HandleAsync(
        GetAssignmentAuthoringChildrenQuery query,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling GetAssignmentAuthoringChildrenQuery {Id}", query.AssignmentId);

        // The aggregate's child navigations are AutoInclude'd by
        // AssignmentConfiguration (questions + their options, attachments, resources),
        // so a single tracked-free read carries every child the editor needs.
        var assignment = await db.Assignments
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == query.AssignmentId, cancellationToken);

        if (assignment is null)
        {
            return null;
        }

        // Persisted collection order is not guaranteed by EF, so the questions are
        // ordered by their DisplayOrder key (the DuplicateAssignmentCommandHandler
        // precedent) — the editor then shows the author's own order.
        var questions = assignment.Questions
            .OrderBy(q => q.DisplayOrder)
            .Select(q => new AssignmentQuestionReadDto(
                q.Id,
                q.QuestionText,
                (QuestionTypeDto)q.QuestionType,
                q.DisplayOrder,
                q.ModelAnswer,
                q.Options.Select(o => new AssignmentQuestionOptionReadDto(o.Id, o.OptionText, o.IsCorrect)).ToList(),
                // R3 (P1-2): so the Edit surface round-trips provenance instead of stripping it.
                q.GenerationId))
            .ToList();

        var attachments = assignment.Attachments
            .Select(a => new AssignmentAttachmentReadDto(
                a.Id,
                a.FileName,
                a.ContentType,
                a.FileSize,
                a.StoragePath,
                // R3 (P1-3): the extraction outcome, so opening Edit does not blank it and the next
                // save re-writes the same values.
                (AttachmentExtractionStatusDto)a.ExtractionStatus,
                a.ExtractedText,
                a.ExtractedAt,
                a.ExtractionError))
            .ToList();

        var resources = assignment.Resources
            .Select(r => new ResourceDto(
                r.Id,
                r.AssignmentId,
                (ResourceKindDto)r.ResourceKind,
                r.Url,
                r.StoragePath,
                r.DisplayName,
                r.IncludedInGeneration))
            .ToList();

        // R2 (D-8.3 / TGT-1): the persisted targeting rows, in the author's DisplayOrder, so the
        // Audience & Targets compartment loads its constraints before enabling — the same
        // fail-closed load-half posture as the question/resource editors (UX-21).
        var targets = assignment.Targets
            .OrderBy(t => t.DisplayOrder)
            .Select(t => new AssignmentTargetDto((TargetKindDto)t.Kind, t.RefId, t.DisplayOrder))
            .ToList();

        return new AssignmentAuthoringChildrenDto(
            assignment.Id,
            questions,
            attachments,
            resources,
            targets,
            // R4 (CP-5/CP-11, OD-2): the picks live on the aggregate (not on a child table), and
            // they ride this read so the Edit surface's pickers start from the persisted set —
            // the same fail-closed load-half posture as the targeting rows above. Always a list
            // (possibly empty), never null: one representation of "no picks".
            assignment.ContextStrandIds,
            assignment.ContextLessonIds);
    }
}
