using SchoolCollab.CodedValues.Core.Domain;

namespace SchoolCollab.MigrationService.Seeding;

/// <summary>
/// Represents a row from the seed-attribute-definitions.csv file.
/// Attribute definitions live on parent coded values and define the schema
/// that children should populate with attribute values.
/// </summary>
public sealed record AttributeDefinitionSeedRow(
    string ParentCode,
    string Key,
    string? DisplayName,
    AttributeDataType DataType,
    string? SourceCode,
    bool IsRequired,
    bool AllowMultiple,
    int? MinLength,
    int? MaxLength,
    string? RegexPattern);