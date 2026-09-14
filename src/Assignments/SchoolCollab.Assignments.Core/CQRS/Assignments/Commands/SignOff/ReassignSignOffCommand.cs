using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SignOff;

/// <summary>
/// WS-C1 (Q5 delegation) — a teacher reassigns the expected signer of a
/// per-(assignment, ward) sign-off to a different linked guardian of the ward.
/// Records the advisory <c>ExpectedSignerGuardianId</c>; the new guardian must
/// be a <see cref="SchoolCollab.Assignments.Core.Services.WardGuardianInfo"/> of
/// the ward (validated cross-context via students-api). Any linked guardian may
/// still sign in v1 (Primary-priority enforcement is recorded backlog).
/// </summary>
public sealed record ReassignSignOffCommand(Guid AssignmentId, Guid StudentId, Guid NewGuardianId) : ICommand;