using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetSubmissionsForReview;

public sealed class GetSubmissionsForReviewHandler(
    ISubmissionRepository submissionRepository,
    ILogger<GetSubmissionsForReviewHandler> logger) : IQueryHandler<GetSubmissionsForReview, SubmissionForReviewDto[]>
{
    public async Task<SubmissionForReviewDto[]> HandleAsync(GetSubmissionsForReview query, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling GetSubmissionsForReview for teacher {TeacherId}", query.TeacherId);
        return await submissionRepository.ListSubmissionsForReviewAsync(query.TeacherId, cancellationToken);
    }
}
