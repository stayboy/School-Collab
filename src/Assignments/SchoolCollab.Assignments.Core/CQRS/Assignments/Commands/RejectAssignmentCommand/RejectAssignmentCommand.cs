using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RejectAssignmentCommand;

/// <summary>Reject a pending assignment (spec §7 Q2). Same
/// identity-posture as <see cref="ApproveAssignmentCommand.ApproverId"/>.</summary>
public sealed record RejectAssignmentCommand(Guid AssignmentId, Guid ApproverId) : ICommand;
