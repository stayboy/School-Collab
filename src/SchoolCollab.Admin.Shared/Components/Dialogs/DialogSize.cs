namespace SchoolCollab.Admin.Shared.Components.Dialogs;

/// <summary>
/// Four fixed widths for <see cref="DialogShellBase{TModel, TResult}"/>-derived
/// dialogs, replacing the ad-hoc CSS strings (<c>"420px"</c>, <c>"560px"</c>)
/// that were scattered across call sites. Keeps every dialog's footprint to a
/// small, reviewed set of sizes so the UI stays consistent and a width change
/// is a one-line edit here rather than a hunt through call sites.
/// </summary>
/// <remarks>
/// <para>The two smallest sizes preserve the exact widths the dialogs used
/// before consolidation, so the migration is pixel-identical:</para>
/// <list type="bullet">
///   <item><term><see cref="Small"/></term><description>420px — small forms (e.g. <c>CodedValueDialog</c> create/override: 3–4 fields). Also the default for <see cref="DialogServiceExtensions.ShowShellDialogAsync"/>.</description></item>
///   <item><term><see cref="Medium"/></term><description>560px — medium forms (e.g. <c>AttributeDefinitionDialog</c>: 9 fields in a label/field row layout).</description></item>
///   <item><term><see cref="Large"/></term><description>720px — larger forms / two-column layouts (no current consumer; reserved for future dialogs).</description></item>
///   <item><term><see cref="ExtraLarge"/></term><description>960px — wide dialogs. Matches the <c>FluentWizard</c> width already used by the grade-level and assignment wizards, so a wide dialog and a wizard occupy the same footprint.</description></item>
/// </list>
/// </remarks>
public enum DialogSize
{
    /// <summary>420px — small forms (3–4 fields). The default dialog size.</summary>
    Small,

    /// <summary>560px — medium forms (e.g. the 9-field attribute-definition dialog).</summary>
    Medium,

    /// <summary>720px — larger forms / two-column layouts. No current consumer; reserved.</summary>
    Large,

    /// <summary>960px — wide dialogs (matches the FluentWizard width).</summary>
    ExtraLarge,
}

/// <summary>
/// <see cref="DialogSize"/> → CSS width string. Used by
/// <see cref="DialogServiceExtensions.BuildShellParameters"/>; exposed for
/// tests and any caller that needs the raw <see cref="DialogParameters.Width"/>.
/// </summary>
public static class DialogSizeExtensions
{
    /// <summary>Returns the CSS width string for <paramref name="size"/> (e.g. <c>"420px"</c>).</summary>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="size"/> is not a defined <see cref="DialogSize"/> value.</exception>
    public static string ToCssWidth(this DialogSize size) => size switch
    {
        DialogSize.Small => "420px",
        DialogSize.Medium => "560px",
        DialogSize.Large => "720px",
        DialogSize.ExtraLarge => "960px",
        _ => throw new System.ArgumentOutOfRangeException(nameof(size), size, $"Unknown {nameof(DialogSize)} value."),
    };
}
