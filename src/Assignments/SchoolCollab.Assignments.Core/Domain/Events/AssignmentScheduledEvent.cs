namespace SchoolCollab.Assignments.Core.Domain.Events;

/// <summary>Raised when an assignment transitions to
/// <see cref="AssignmentStatus.Scheduled"/> (spec §3.5 step 2).
/// Carries the assignment id + title for diagnostic logging — the
/// sweep dispatches <see cref="SchoolCollab.Assignments.Core.Domain.Assignment.Publish"/>
/// when the scheduled moment arrives, so the broadcast notice lands
/// through the existing <c>AssignmentPublishedEvent</c>.</summary>
public sealed record AssignmentScheduledEvent(Guid AssignmentId, string Title) : IDomainEvent;
