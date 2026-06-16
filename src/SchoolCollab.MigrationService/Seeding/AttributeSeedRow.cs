namespace SchoolCollab.MigrationService.Seeding;

/// <summary>
/// Represents a row from the seed-attributes.csv file.
/// Attribute values live on child coded values and fill in the definitions
/// defined on their parent.
/// </summary>
public sealed record AttributeSeedRow(
    string Code,
    string Key,
    string Value);