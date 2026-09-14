using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.SignOff;

/// <summary>
/// WS-C1 — list the per-(assignment, ward) guardian sign-off status rows for the
/// teacher surface (spec §3.2 line 51). Empty when the assignment does not
/// require a signature (the card never renders for such assignments — the query
/// is defensive). Names are resolved cross-context via <c>IStudentDirectory</c>.
/// </summary>
public sealed record ListSignOffStatusesQuery(Guid AssignmentId) : IQuery<IReadOnlyList<SignOffStatusDto>>;
