namespace SchoolCollab.Settings.Contracts.Events;

public record CodedValueEnabled(Guid Id, string Code, DateTimeOffset EnabledAt);
