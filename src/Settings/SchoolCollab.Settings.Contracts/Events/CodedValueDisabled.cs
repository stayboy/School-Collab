namespace SchoolCollab.Settings.Contracts.Events;

public record CodedValueDisabled(Guid Id, string Code, DateTimeOffset DisabledAt);
