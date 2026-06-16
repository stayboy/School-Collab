namespace SchoolCollab.Assignments.Core.Domain.Events;

public sealed record AssignmentCreatedEvent(Guid AssignmentId, string Title) : IDomainEvent;