namespace SchoolCollab.CodedValues.Core.Domain.Events;

public record CodedValueEnabledEvent(Guid Id, string Code) : IDomainEvent;
