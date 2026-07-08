using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Admin.Shared.Components;

/// <summary>
/// Form-state model for <see cref="CodedValueDialog"/>. Replaces the old
/// <c>CodedValueDialogData</c> + <c>CodedValueDialogResult</c> pair — the
/// dialog is now a <see cref="Dialogs.DialogShellBase{TModel, TResult}"/>
/// whose <c>TResult</c> is <see cref="CodedValueDto"/> (returned via
/// <see cref="Dialogs.DialogShellResult{TResult}"/>), so no per-dialog
/// result record is needed.
/// </summary>
/// <param name="Mode"><c>"Create"</c> or <c>"Override"</c> — selects the form fields rendered. Kept as a string (not a discriminated union) to mirror the pre-existing <c>CodedValueDialogData.Mode</c> shape; splitting into separate Create/Override components is an explicit non-goal of the consolidation.</param>
/// <param name="ParentId">Create mode: the parent coded value id to create under.</param>
/// <param name="CodedValue">Override mode: the coded value being overridden.</param>
/// <param name="HasOverride">Override mode: whether a tenant override already exists (controls the "Reset to default" button).</param>
public sealed record CodedValueFormModel(
    string Mode,
    Guid? ParentId,
    CodedValueDto? CodedValue,
    bool HasOverride = false)
{
    /// <summary>Bindable (Create + Override). Code is Create-only in the UI but lives on the shared model.</summary>
    public string? Code { get; set; }

    /// <summary>Bindable (Create + Override). Required for Create; optional for Override (empty = use default name).</summary>
    public string? Name { get; set; }

    /// <summary>Bindable (Create + Override). Optional.</summary>
    public string? Description { get; set; }

    /// <summary>Bindable (Create only).</summary>
    public int? DisplayOrder { get; set; }

    /// <summary>Factory for the Create flow.</summary>
    public static CodedValueFormModel ForCreate(Guid? parentId) => new("Create", parentId, null);

    /// <summary>Factory for the Override flow.</summary>
    public static CodedValueFormModel ForOverride(CodedValueDto codedValue, bool hasOverride) =>
        new("Override", codedValue.ParentId, codedValue, hasOverride);
}
