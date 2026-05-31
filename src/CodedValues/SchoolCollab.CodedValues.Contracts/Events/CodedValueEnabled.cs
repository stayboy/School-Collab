namespace SchoolCollab.CodedValues.Contracts.Events;

public record CodedValueEnabled(Guid Id, string Code, DateTimeOffset EnabledAt);
