namespace SchoolCollab.Assignments.Core.Domain.Events;

/// <summary>Raised when an assignment transitions to
/// <see cref="AssignmentStatus.Archived"/> (spec §7 Q6 — read-only
/// retention). No integration event is published — the round
/// confines itself to the in-process domain event (decisions (c)/(g)).</summary>
public sealed record AssignmentArchivedEvent(Guid AssignmentId, string Title) : IDomainEvent;
