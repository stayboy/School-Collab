namespace SchoolCollab.Settings.Contracts.Events;

public record CodedValueCreated(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? ParentId,
    int DisplayOrder,
    DateTimeOffset CreatedAt);
