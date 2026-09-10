namespace SchoolCollab.Assignments.Core.Domain.Events;

/// <summary>Raised when an assignment moves into
/// <see cref="ApprovalStatus.Pending"/> via
/// <c>Assignment.SubmitForApproval()</c> (spec §7 Q2 — submitted for
/// approval but not yet approved).</summary>
public sealed record AssignmentApprovalSubmittedEvent(Guid AssignmentId, string Title) : IDomainEvent;
