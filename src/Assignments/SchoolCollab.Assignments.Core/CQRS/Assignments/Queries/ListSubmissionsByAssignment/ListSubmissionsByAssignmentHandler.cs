using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data.Repositories;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListSubmissionsByAssignment;

public sealed class ListSubmissionsByAssignmentHandler(
    ISubmissionRepository submissionRepository,
    ILogger<ListSubmissionsByAssignmentHandler> logger) : IQueryHandler<ListSubmissionsByAssignment, SubmissionForReviewDto[]>
{
    public async Task<SubmissionForReviewDto[]> HandleAsync(ListSubmissionsByAssignment query, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ListSubmissionsByAssignment for assignment {AssignmentId}", query.AssignmentId);
        return await submissionRepository.ListSubmissionsByAssignmentAsync(query.AssignmentId, cancellationToken);
    }
}