using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetSubmissionsForReview;

/// <summary>
/// Teacher review queue: submissions for assignments owned by
/// <see cref="GetSubmissionsForReview.TeacherId"/> (spec §4.13).
/// </summary>
public sealed record GetSubmissionsForReview(Guid TeacherId) : IQuery<SubmissionForReviewDto[]>;
