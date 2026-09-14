using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.SignOff;

/// <summary>
/// WS-C1 — lists the per-ward guardian sign-off status rows for one assignment
/// (spec §3.2 line 51 + §6 auditability). Projection lives in the shared
/// <see cref="SignOffStatusProjection"/> (the sign command returns a refreshed
/// row through the same builder). Empty when the assignment does not require a
/// signature; names degrade to the raw id on a directory miss.
/// </summary>
public sealed class ListSignOffStatusesQueryHandler(
    IAssignmentRepository assignmentRepository,
    ISubmissionRepository submissionRepository,
    IStudentDirectory studentDirectory,
    ILogger<ListSignOffStatusesQueryHandler> logger) : IQueryHandler<ListSignOffStatusesQuery, IReadOnlyList<SignOffStatusDto>>
{
    public async Task<IReadOnlyList<SignOffStatusDto>> HandleAsync(
        ListSignOffStatusesQuery query, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Listing sign-off statuses for assignment {AssignmentId}", query.AssignmentId);

        var assignment = await assignmentRepository.GetAsync(query.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(query.AssignmentId);

        // Defensive: the card never renders for non-signature assignments.
        if (!assignment.RequiresSignature)
        {
            return [];
        }

        return await SignOffStatusProjection.BuildAsync(
            query.AssignmentId, assignmentRepository, submissionRepository, studentDirectory, logger, cancellationToken);
    }
}