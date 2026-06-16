namespace SchoolCollab.Assignments.Core.Domain.Events;

public sealed record AssignmentUnpublishedEvent(Guid AssignmentId, string Title) : IDomainEvent;