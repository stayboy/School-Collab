using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ApproveAssignmentCommand;

/// <summary>Approve a pending assignment (spec §7 Q2).
/// <see cref="ApproverId"/> is a placeholder until identity wiring
/// lands (the <c>ReviewAssignmentRequest.TeacherId</c> posture).</summary>
public sealed record ApproveAssignmentCommand(Guid AssignmentId, Guid ApproverId) : ICommand;
