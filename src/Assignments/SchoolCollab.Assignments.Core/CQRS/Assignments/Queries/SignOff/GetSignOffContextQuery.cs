using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.SignOff;

/// <summary>
/// WS-C2 — the single aggregate the guardian e-sign page consumes (spec §3.2).
/// Carries the review summary, the resolved consent text, and the ward's linked
/// guardians. The page makes exactly this one cross-context call.
/// </summary>
public sealed record GetSignOffContextQuery(Guid AssignmentId, Guid StudentId) : IQuery<SignOffContextDto>;
