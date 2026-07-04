namespace SchoolCollab.Settings.Core.Domain.Events;

public record CodedValueEnabledEvent(Guid Id, string Code) : IDomainEvent;
