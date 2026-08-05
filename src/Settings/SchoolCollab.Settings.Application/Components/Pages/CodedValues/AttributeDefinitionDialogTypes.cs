using System.ComponentModel.DataAnnotations;
using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Settings.Application.Components.Pages.CodedValues;

public record DataTypeOption(string Label, AttributeDataType Value);

public record ParentCodedValueOption(Guid Id, string Code, string Name);

/// <summary>
/// Form-state model for <see cref="AttributeDefinitionDialog"/>. Replaces
/// the old <c>AttributeDefinitionDialogData</c> — the dialog is now a
/// <see cref="SchoolCollab.Admin.Shared.Components.Dialogs.DialogShellBase{TModel, TResult}"/>
/// whose <c>TResult</c> is <see cref="AttributeDefinitionResult"/>.
///
/// <para>The old <c>Api</c>/<c>CodedValueId</c> fields are gone: the dialog
/// never called the API itself (it only returned a result object); the
/// caller (<c>Edit.razor</c>) persists it. Only the two inputs the dialog
/// body actually reads survive as <see cref="ExistingDefinition"/> and
/// <see cref="ParentValues"/>.</para>
/// </summary>
/// <param name="ExistingDefinition">Edit mode: the definition being edited. Create mode: <c>null</c>.</param>
/// <param name="ParentValues">Candidate parent coded values for the Source Code dropdown (the current item excluded by the caller).</param>
public sealed record AttributeDefinitionFormModel(
    CodedValueAttributeDefinitionDto? ExistingDefinition,
    CodedValueDto[]? ParentValues)
{
    /// <summary>Bindable. Required (validated); also re-checked for whitespace in SubmitAsync since [Required] lets whitespace through.</summary>
    [Required]
    public string? Key { get; set; }

    /// <summary>Bindable. Optional display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Bindable.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Bindable.</summary>
    public bool AllowMultiple { get; set; }

    /// <summary>Bindable. Optional.</summary>
    public int? MinLength { get; set; }

    /// <summary>Bindable. Optional.</summary>
    public int? MaxLength { get; set; }

    /// <summary>Bindable. Optional regex pattern.</summary>
    public string? RegexPattern { get; set; }

    /// <summary>Factory for the Add flow.</summary>
    public static AttributeDefinitionFormModel ForCreate(CodedValueDto[]? parentValues) => new(null, parentValues);

    /// <summary>Factory for the Edit flow.</summary>
    public static AttributeDefinitionFormModel ForEdit(CodedValueAttributeDefinitionDto existing, CodedValueDto[]? parentValues) =>
        new(existing, parentValues);
}

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
