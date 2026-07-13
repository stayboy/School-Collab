using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListSubmissionsByAssignment;

/// <summary>List every submission for an assignment (spec §8; teacher review/grade queue in §12).</summary>
public sealed record ListSubmissionsByAssignment(Guid AssignmentId) : IQuery<SubmissionForReviewDto[]>;