using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data.Repositories;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetSubmission;

public sealed class GetSubmissionHandler(
    ISubmissionRepository submissionRepository,
    ILogger<GetSubmissionHandler> logger) : IQueryHandler<GetSubmission, SubmissionDetailDto?>
{
    public async Task<SubmissionDetailDto?> HandleAsync(GetSubmission query, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling GetSubmission for assignment {AssignmentId} / student {StudentId}",
            query.AssignmentId, query.StudentId);
        return await submissionRepository.GetSubmissionDetailAsync(query.AssignmentId, query.StudentId, cancellationToken);
    }
}