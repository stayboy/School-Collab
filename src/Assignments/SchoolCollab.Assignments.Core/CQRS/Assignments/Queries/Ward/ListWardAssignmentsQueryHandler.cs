using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.Ward;

/// <summary>
/// WS-A5 — projects the ward assignment list. The candidate set (visible
/// status + recipient-link targeting) comes from
/// <see cref="IWardAssignmentProjectionRepository.ListWardAssignmentsAsync"/>
/// (ar-13 A-1 cohesion fix: a dedicated ward-aggregation read, split out of the
/// module-progress repository); each row is enriched with its required-module
/// lock state (per-module progress) and submission state (None → InProgress →
/// Completed via FinalizedAt), the ar-9 per-ward projection precedent. N+1 over
/// the candidate set is accepted for v1 (the 2b list UI is low-volume per ward)
/// — recorded in the round doc.
/// </summary>
public sealed class ListWardAssignmentsHandler(
    IAssignmentRepository assignmentRepository,
    ISubmissionRepository submissionRepository,
    IWardAssignmentProjectionRepository wardAssignmentProjectionRepository,
    IModuleProgressRepository moduleProgressRepository,
    ILogger<ListWardAssignmentsHandler> logger) : IQueryHandler<ListWardAssignments, WardAssignmentListItemDto[]>
{
    public async Task<WardAssignmentListItemDto[]> HandleAsync(ListWardAssignments query, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ListWardAssignments for student {StudentId}", query.StudentId);

        var summaries = await wardAssignmentProjectionRepository.ListWardAssignmentsAsync(query.StudentId, DateTimeOffset.UtcNow, cancellationToken);

        var result = new List<WardAssignmentListItemDto>(summaries.Count);
        foreach (var summary in summaries)
        {
            var assignment = await assignmentRepository.GetAsync(summary.Id, cancellationToken);
            if (assignment is null)
            {
                continue;
            }

            var progressByModule = (await moduleProgressRepository.ListProgressForAssignmentStudentAsync(
                summary.Id, query.StudentId, cancellationToken))
                .ToDictionary(p => p.ContentModuleId);

            var hasLockedModules = assignment.Modules
                .Where(m => m.IsRequired)
                .Any(m => !progressByModule.TryGetValue(m.Id, out var p) || p.CompletedAt is null);

            var submission = await submissionRepository.GetSubmissionByAssignmentStudentAsync(
                summary.Id, query.StudentId, cancellationToken);
            var state = submission is null
                ? WardSubmissionStateDto.NotStarted
                : submission.FinalizedAt is not null
                    ? WardSubmissionStateDto.Completed
                    : WardSubmissionStateDto.InProgress;

            result.Add(new WardAssignmentListItemDto(
                summary.Id, summary.Title, summary.DueDate, state, hasLockedModules));
        }

        return result.ToArray();
    }
}
