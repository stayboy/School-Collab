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
                q.Options.Select(o => new AssignmentQuestionOptionReadDto(o.Id, o.OptionText, o.IsCorrect)).ToList()))
            .ToList();

        var attachments = assignment.Attachments
            .Select(a => new AssignmentAttachmentReadDto(a.Id, a.FileName, a.ContentType, a.FileSize, a.StoragePath))
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

        return new AssignmentAuthoringChildrenDto(assignment.Id, questions, attachments, resources);
    }
}
