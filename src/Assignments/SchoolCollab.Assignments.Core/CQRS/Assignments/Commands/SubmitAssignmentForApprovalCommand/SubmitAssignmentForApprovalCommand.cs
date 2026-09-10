using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentForApprovalCommand;

/// <summary>Submit a Draft assignment for approval (spec §7 Q2).
/// Only valid when the row is in <c>Draft</c> status — the domain
/// guards the transition.</summary>
public sealed record SubmitAssignmentForApprovalCommand(Guid AssignmentId) : ICommand;
