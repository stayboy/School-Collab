namespace SchoolCollab.Admin.Shared.Components;

/// <summary>
/// Nine fixed widths for form inputs, giving every field across every form
/// and dialog a single, reviewed sizing ladder instead of ad-hoc per-component
/// pixel values. The companion <see cref="FieldWidthExtensions.ToCssStyle"/>
/// produces an inline <c>style</c> string consumed by the repo's dropdown
/// wrappers (<see cref="DropdownForEnum"/>, <see cref="CodedValueDropdown"/>,
/// <see cref="DropdownComponent"/>); the matching <c>w-1</c>…<c>w-9</c> CSS
/// classes in <c>src/SchoolCollab.Admin/wwwroot/css/app.css</c> cover
/// third-party inputs (<c>FluentTextField</c>, direct <c>FluentSelect</c>)
/// that the repo does not wrap. Both expressions of the ladder MUST stay in
/// sync — see <see cref="FieldWidthExtensions.ToCssStyle"/> for the values.
/// </summary>
/// <remarks>
/// <para>The ladder is anchored to the pixel values already used across the
/// repo's forms so migration is low-friction:</para>
/// <list type="table">
///   <listheader><term>Step</term><description>Width / typical use</description></listheader>
///   <item><term><see cref="W1"/></term><description>80px — Title/Salutation, Channel enum, tiny fields.</description></item>
///   <item><term><see cref="W2"/></term><description>120px — Country calling code.</description></item>
///   <item><term><see cref="W3"/></term><description>160px — Date picker (matches the student DOB field).</description></item>
///   <item><term><see cref="W4"/></term><description>200px — Phone, display-order, contact Label.</description></item>
///   <item><term><see cref="W5"/></term><description>240px — Student / ID number.</description></item>
///   <item><term><see cref="W6"/></term><description>280px — Gender, Relationship.</description></item>
///   <item><term><see cref="W7"/></term><description>320px — Medium text field.</description></item>
///   <item><term><see cref="W8"/></term><description>400px — Long text field.</description></item>
///   <item><term><see cref="W9"/></term><description>Fill — email, textarea, "fill the FormRow input cell" (width:100% + flex:1 1 0).</description></item>
/// </list>
/// <para><b>Why an enum + inline style on the wrappers (not a CSS class)?</b>
/// Blazor scoped CSS compiles each wrapper's base class (e.g.
/// <c>.coded-value-dropdown</c>) to <c>.coded-value-dropdown[b-hash]</c> —
/// specificity (0,2,0) (class + attribute). A global <c>.w-1</c> class is
/// (0,1,0) and the scoped bundle is linked after the global <c>app.css</c>,
/// so the scoped <c>width:100%</c> would silently override any <c>w-N</c>
/// class. Emitting an inline <c>style</c> (specificity 1,0,0,0) from the
/// wrapper's <see cref="FluentSelect.Style"/> parameter always wins, with no
/// <c>!important</c> and no edits to the wrappers' scoped CSS. This is the
/// repo convention's documented exception ("dynamic values computed in C#").</para>
/// </remarks>
public enum FieldWidth
{
    /// <summary>80px — Title/Salutation, Channel enum, tiny fields.</summary>
    W1,

    /// <summary>120px — Country calling code.</summary>
    W2,

    /// <summary>160px — Date picker (matches the student DOB field).</summary>
    W3,

    /// <summary>200px — Phone, display-order, contact Label.</summary>
    W4,

    /// <summary>240px — Student / ID number.</summary>
    W5,

    /// <summary>280px — Gender, Relationship.</summary>
    W6,

    /// <summary>320px — Medium text field.</summary>
    W7,

    /// <summary>400px — Long text field.</summary>
    W8,

    /// <summary>Fill — email, textarea, "fill the FormRow input cell" (width:100% + flex:1 1 0).</summary>
    W9,
}

/// <summary>
/// <see cref="FieldWidth"/> → CSS <c>style</c> string. Used by the dropdown
/// wrappers to emit an inline style on their underlying
/// <see cref="Microsoft.FluentUI.AspNetCore.Components.FluentSelect"/> (inline
/// style beats Blazor scoped CSS, so the width always wins over the
/// wrapper's default <c>width:100%</c>). The same pixel values are encoded as
/// the <c>w-1</c>…<c>w-9</c> classes in
/// <c>src/SchoolCollab.Admin/wwwroot/css/app.css</c> for third-party inputs —
/// keep the two in sync.
/// </summary>
public static class FieldWidthExtensions
{
    /// <summary>
    /// Returns the inline CSS <c>style</c> string for <paramref name="width"/>,
    /// e.g. <c>"width:80px; min-width:0; max-width:100%"</c>. <c>min-width:0</c>
    /// lets <c>&lt;fluent-select&gt;</c>/<c>&lt;fluent-combobox&gt;</c> honor
    /// widths below their intrinsic minimum; <c>max-width:100%</c> prevents a
    /// long value from blowing out its column.
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="width"/> is not a defined <see cref="FieldWidth"/> value.</exception>
    public static string ToCssStyle(this FieldWidth width) => width switch
    {
        FieldWidth.W1 => "width:80px; min-width:0; max-width:100%",
        FieldWidth.W2 => "width:120px; min-width:0; max-width:100%",
        FieldWidth.W3 => "width:160px; min-width:0; max-width:100%",
        FieldWidth.W4 => "width:200px; min-width:0; max-width:100%",
        FieldWidth.W5 => "width:240px; min-width:0; max-width:100%",
        FieldWidth.W6 => "width:280px; min-width:0; max-width:100%",
        FieldWidth.W7 => "width:320px; min-width:0; max-width:100%",
        FieldWidth.W8 => "width:400px; min-width:0; max-width:100%",
        FieldWidth.W9 => "width:100%; min-width:0; max-width:100%; flex:1 1 0",
        _ => throw new System.ArgumentOutOfRangeException(nameof(width), width, $"Unknown {nameof(FieldWidth)} value."),
    };
}