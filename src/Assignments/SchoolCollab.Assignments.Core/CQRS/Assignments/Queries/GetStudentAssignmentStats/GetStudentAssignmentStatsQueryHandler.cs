using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetStudentAssignmentStats;

/// <summary>
/// Issue #444 (layer L2a) — folds the student's recipient-linked assignment rows into the six counts of
/// <see cref="StudentAssignmentStatsDto"/> in one repository call (decision A(ii): the rules stay in
/// testable C# rather than in a SQL <c>GROUP BY</c>).
/// <para>
/// The read's candidate set is recipient-link only, so <c>Assigned</c> is wider than <c>SentOut</c>: the
/// sent test (Q126) is applied HERE. <c>Marked</c> requires the same submission to be finalized AND
/// <see cref="ReviewState.Graded"/> — <c>Graded</c> alone is reachable without a finalization, so grading
/// on its own would let <c>Marked</c> exceed <c>Submitted</c>. The clock comes from the injected
/// <see cref="TimeProvider"/>, never <see cref="DateTimeOffset.UtcNow"/>, so the scheduled boundary is
/// assertable.
/// </para>
/// </summary>
public sealed class GetStudentAssignmentStatsQueryHandler(
    IStudentAssignmentStatsRepository statsRepository,
    TimeProvider timeProvider) : IQueryHandler<GetStudentAssignmentStats, StudentAssignmentStatsDto>
{
    public async Task<StudentAssignmentStatsDto> HandleAsync(GetStudentAssignmentStats query, CancellationToken cancellationToken = default)
    {
        var rows = await statsRepository.ListStudentAssignmentStatRowsAsync(query.StudentId, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow();

        var sentOut = 0;
        var submitted = 0;
        var marked = 0;
        var reviewRequired = 0;
        var reviewed = 0;

        foreach (var row in rows)
        {
            if (!IsSentOut(row, nowUtc))
            {
                continue;
            }

            sentOut++;

            // The review axis's own denominator: only a SENT, mandatory-review assignment is gated, so
            // ReviewRequired can never exceed SentOut.
            if (row.MandatoryReview)
            {
                reviewRequired++;
                if (row.HasReviewedGate)
                {
                    reviewed++;
                }
            }

            if (row.HasFinalizedSubmission)
            {
                submitted++;
                if (row.ReviewState == ReviewState.Graded)
                {
                    marked++;
                }
            }
        }

        return new StudentAssignmentStatsDto(
            Assigned: rows.Count,
            SentOut: sentOut,
            Submitted: submitted,
            Marked: marked,
            ReviewRequired: reviewRequired,
            Reviewed: reviewed);
    }

    /// <summary>Q126 — "sent out" is <c>Published</c>, or <c>Scheduled</c> whose publish moment has passed.
    /// The same predicate the ward read applies in SQL, restated here because this read must NOT filter
    /// statuses in SQL.</summary>
    private static bool IsSentOut(StudentAssignmentStatRow row, DateTimeOffset nowUtc)
        => row.Status switch
        {
            AssignmentStatus.Published => true,
            AssignmentStatus.Scheduled => row.AvailableFromUtc is not null && row.AvailableFromUtc <= nowUtc,
            _ => false,
        };
}
