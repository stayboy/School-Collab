namespace SchoolCollab.CodedValues.Contracts.Events;

public record CodedValueDisabled(Guid Id, string Code, DateTimeOffset DisabledAt);
