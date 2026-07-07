namespace SchoolCollab.Students.Core.Domain.Events;

public sealed record GradeLevelDeletedEvent(Guid Id, string Name) : IDomainEvent;