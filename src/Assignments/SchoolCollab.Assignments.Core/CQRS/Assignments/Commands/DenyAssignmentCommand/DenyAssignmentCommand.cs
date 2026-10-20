using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.DenyAssignmentCommand;

/// <summary>Deny a pending assignment (round assignment-approval-workflow / spec §3.1, Q10) — the
/// <b>terminal</b> decision: the pending-approval edit hold is released, the row can never be
/// resubmitted or published, and duplicating it is the author's escape hatch. Same identity posture
/// as the approve command (principal-wins, ar-20; cross-tenant ⇒ ar-24 rejection);
/// <see cref="Reason"/> is the approver's optional free-form note, stored on the row and shown to
/// the author.</summary>
public sealed record DenyAssignmentCommand(Guid AssignmentId, Guid ApproverId, string? Reason = null) : ICommand;
