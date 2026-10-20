using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RejectAssignmentCommand;

/// <summary>Reject a pending assignment (spec §7 Q2) — "sent back for redo": the row becomes
/// editable and resubmittable. Same identity-posture as the approve command; <see cref="Reason"/> is
/// the approver's optional free-form note, stored on the row and shown to the author (round
/// assignment-approval-workflow / spec §3.1, Q10).</summary>
public sealed record RejectAssignmentCommand(Guid AssignmentId, Guid ApproverId, string? Reason = null) : ICommand;
