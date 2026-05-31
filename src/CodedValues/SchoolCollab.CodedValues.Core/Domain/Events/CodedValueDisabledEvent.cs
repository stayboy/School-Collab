namespace SchoolCollab.CodedValues.Core.Domain.Events;

public record CodedValueDisabledEvent(Guid Id, string Code) : IDomainEvent;
