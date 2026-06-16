namespace SchoolCollab.Assignments.Core.Domain.Events;

public sealed record AssignmentClosedEvent(Guid AssignmentId, string Title) : IDomainEvent;