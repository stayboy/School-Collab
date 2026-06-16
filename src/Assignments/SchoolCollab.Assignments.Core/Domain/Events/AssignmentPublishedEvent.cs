namespace SchoolCollab.Assignments.Core.Domain.Events;

public sealed record AssignmentPublishedEvent(Guid AssignmentId, string Title) : IDomainEvent;