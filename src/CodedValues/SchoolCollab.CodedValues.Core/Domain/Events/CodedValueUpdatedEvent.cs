namespace SchoolCollab.CodedValues.Core.Domain.Events;

public record CodedValueUpdatedEvent(Guid Id, string Code, string Name) : IDomainEvent;
