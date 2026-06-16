namespace SchoolCollab.MigrationService.Seeding;

public sealed record CodedValueSeedRow(
    string Code,
    string Name,
    string? Description,
    string? ParentCode,
    int DisplayOrder);