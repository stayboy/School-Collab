namespace SchoolCollab.Settings.Contracts.Events;

public record CodedValueUpdated(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    DateTimeOffset UpdatedAt);
