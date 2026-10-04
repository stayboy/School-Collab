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
        // [P1-4] an EMPTY scope grants nothing: return an empty queue rather than let the
        // teacherId the route threaded in (the dev fallback) decide. This is the containment that
        // makes "teacher without a teacher_id claim" fail closed — never tenant-wide, and never
        // the wire value.
        if (query.Scope is { IsEmpty: true })
        {
            logger.LogDebug(
                "GetSubmissionsForReview for teacher {TeacherId} resolves to an empty scope; returning an empty queue",
                query.TeacherId);
            return [];
        }

        logger.LogDebug("Handling GetSubmissionsForReview for teacher {TeacherId}", query.TeacherId);
        return await submissionRepository.ListSubmissionsForReviewAsync(query.TeacherId, query.Scope, cancellationToken);
    }
}
