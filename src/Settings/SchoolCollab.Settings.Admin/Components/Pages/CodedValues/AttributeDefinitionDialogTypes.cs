using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Settings.Admin.Components.Pages.CodedValues;

public record DataTypeOption(string Label, AttributeDataType Value);

public record ParentCodedValueOption(Guid Id, string Code, string Name);

public record AttributeDefinitionDialogData(
    CodedValuesApiClient Api,
    Guid CodedValueId,
    CodedValueAttributeDefinitionDto? ExistingDefinition = null,
    CodedValueDto[]? ParentValues = null);

public record AttributeDefinitionResult(
    string Key,
    string? DisplayName,
    AttributeDataType DataType,
    string? SourceCode,
    bool IsRequired,
    bool AllowMultiple,
    int? MinLength,
    int? MaxLength,
    string? RegexPattern);