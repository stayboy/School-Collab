namespace SchoolCollab.Assignments.Core.Domain.Events;

public sealed record AssignmentUpdatedEvent(Guid AssignmentId, string Title) : IDomainEvent;