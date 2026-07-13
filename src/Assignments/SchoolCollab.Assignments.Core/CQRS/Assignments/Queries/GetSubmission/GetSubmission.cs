using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetSubmission;

/// <summary>Get a submission (with version history + review) for an assignment/student pair (spec §8/§9).</summary>
public sealed record GetSubmission(Guid AssignmentId, Guid StudentId) : IQuery<SubmissionDetailDto?>;