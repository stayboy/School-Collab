namespace SchoolCollab.CodedValues.Core.Domain.Events;

public record CodedValueCreatedEvent(Guid Id, string Code, string Name, Guid? ParentId) : IDomainEvent;
