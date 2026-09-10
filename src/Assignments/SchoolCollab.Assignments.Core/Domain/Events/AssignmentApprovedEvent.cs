namespace SchoolCollab.Assignments.Core.Domain.Events;

/// <summary>Raised when an assignment is approved via
/// <c>Assignment.Approve(Guid)</c> (spec §7 Q2). Carries the
/// approver id so future audit-side consumers can attribute the
/// decision — the API surface passes the approver id from the
/// request (a Guid.Empty placeholder until identity wiring lands).</summary>
public sealed record AssignmentApprovedEvent(Guid AssignmentId, Guid ApproverId) : IDomainEvent;
