namespace SchoolCollab.Students.Core.Domain.Events;

public sealed record TopicDeletedEvent(Guid Id, string? Code) : IDomainEvent;