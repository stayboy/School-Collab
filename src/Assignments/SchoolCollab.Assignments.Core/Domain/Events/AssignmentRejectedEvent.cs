namespace SchoolCollab.Assignments.Core.Domain.Events;

/// <summary>Raised when an assignment is rejected via
/// <c>Assignment.Reject(Guid)</c> (spec §7 Q2). Carries the
/// approver id — see <see cref="AssignmentApprovedEvent"/> for
/// the identity-posture note.</summary>
public sealed record AssignmentRejectedEvent(Guid AssignmentId, Guid ApproverId) : IDomainEvent;
