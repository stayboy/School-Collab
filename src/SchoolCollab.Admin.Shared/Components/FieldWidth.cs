namespace SchoolCollab.Admin.Shared.Components;

/// <summary>
/// Nine fixed widths for form inputs, giving every field across every form
/// and dialog a single, reviewed sizing ladder instead of ad-hoc per-component
/// pixel values. The companion <see cref="FieldWidthExtensions.ToCssStyle"/>
/// produces an inline <c>style</c> string consumed by the repo's dropdown
/// wrappers (<see cref="DropdownForEnum"/>, <see cref="CodedValueDropdown"/>,
/// <see cref="DropdownComponent"/>); the matching <c>w-1</c>…<c>w-9</c> CSS
/// classes in <c>src/SchoolCollab.Admin/wwwroot/css/app.css</c> cover
/// third-party inputs (<c>FluentTextInput</c>, direct <c>FluentSelect</c>)
/// that the repo does not wrap. Both expressions of the ladder MUST stay in
/// sync — see <see cref="FieldWidthExtensions.ToCssStyle"/> for the values.
/// </summary>
/// <remarks>
/// <para>The ladder is anchored to the pixel values already used across the
/// repo's forms so migration is low-friction:</para>
/// <list type="table">
///   <listheader><term>Step</term><description>Width / typical use</description></listheader>
///   <item><term><see cref="W1"/></term><description>80px — tiny single-line text fields (never a dropdown, see below).</description></item>
///   <item><term><see cref="W2"/></term><description>120px — short single-line text fields (never a dropdown, see below).</description></item>
///   <item><term><see cref="W3"/></term><description>160px — Date picker (matches the student DOB field); the narrowest legal DROPDOWN.</description></item>
///   <item><term><see cref="W4"/></term><description>200px — Phone, display-order, contact Label.</description></item>
///   <item><term><see cref="W5"/></term><description>240px — Student / ID number.</description></item>
///   <item><term><see cref="W6"/></term><description>280px — Gender, Relationship.</description></item>
///   <item><term><see cref="W7"/></term><description>320px — Medium text field.</description></item>
///   <item><term><see cref="W8"/></term><description>400px — Long text field.</description></item>
///   <item><term><see cref="W9"/></term><description>Fill — email, textarea, "fill the FormRow input cell" (width:100% + flex:1 1 auto; see <see cref="W9"/>).</description></item>
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
/// <para><b>v5 splits that inline style's target in two.</b> The
/// <c>FluentSelect</c> renders as
/// <c>&lt;fluent-field class="…" style="width:…"&gt;&lt;fluent-dropdown slot="input"&gt;</c>,
/// so the inline style lands on the <c>fluent-field</c> HOST, not on the
/// control. The control is sized from the host by
/// <c>fluent-field &gt; fluent-dropdown { width: inherit; }</c> in
/// <c>src/SchoolCollab.Admin.Shared/wwwroot/app.css</c> (owner pin Q28(a),
/// 2026-10-16) — remove either half and the ladder sizes only the wrapper.</para>
/// <para><b>A DROPDOWN IS NEVER SIZED BELOW <see cref="W3"/> (owner, 2026-10-24).</b> Fluent v5
/// hard-codes <c>min-width: 160px</c> on the <c>fluent-dropdown</c> shadow <c>.control</c>, and the
/// host keeps <c>overflow: visible</c>, so a narrower host does not shrink the control — it PAINTS
/// PAST it and covers the neighbouring field. Measured: the contact channel dropdown at <c>W2</c>
/// (120px) painted 160px, 40px over the “Label (optional)” input, and hit-testing at that input's
/// left edge returned <c>fluent-dropdown</c>. That is why the ladder's three <c>ContactChannel</c>
/// sites moved to <see cref="W3"/>; the floor is not enforceable in CSS (the value is inside the
/// web component's shadow root), so it is a review rule: <see cref="W1"/> and <see cref="W2"/> are
/// for single-line text inputs only. A shared <c>overflow: hidden</c> clip was rejected — it would
/// hide the next too-narrow dropdown instead of failing loudly.</para>
/// </remarks>
public enum FieldWidth
{
    /// <summary>80px — tiny single-line text fields. Not for dropdowns (floor is <see cref="W3"/>).</summary>
    W1,

    /// <summary>120px — short single-line text fields. Not for dropdowns (floor is <see cref="W3"/>).</summary>
    W2,

    /// <summary>160px — Date picker (matches the student DOB field); the narrowest legal dropdown.</summary>
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

    /// <summary>
    /// Fill — email, textarea, "fill the FormRow input cell": <c>width:100%</c> plus
    /// <c>flex:1 1 auto</c>.
    /// <para><b>The <c>auto</c> basis is load-bearing — do not "tidy" it back to <c>1 1 0</c>.</b>
    /// <c>.form-row-input</c> (the input cell in <c>FormRow.razor.css</c>) is a <c>display:flex</c>
    /// container, so this style applies to a flex ITEM. With a zero flex-basis the used width is
    /// resolved by the flex algorithm from the item's content, not from the <c>100%</c>, so a narrow
    /// cell sized the field from its text and W9 read as the same width as a fixed <see cref="W4"/>
    /// sibling (#414, measured 2026-10-27). An <c>auto</c> basis makes the <c>100%</c> the starting
    /// size, so the field fills the cell before growing; in a non-flex parent the <c>100%</c> alone
    /// still fills the block. This mirrors <c>.w-9</c> in
    /// <c>src/SchoolCollab.Admin/wwwroot/css/app.css</c>, where the same cause is recorded — the two
    /// expressions of the ladder must agree.</para>
    /// </summary>
    W9,
}

/// <summary>
/// <see cref="FieldWidth"/> → CSS <c>style</c> string. Used by the dropdown
/// wrappers to emit an inline style on the <c>fluent-field</c> host v5 renders
/// around their <see cref="Microsoft.FluentUI.AspNetCore.Components.FluentSelect"/>
/// (inline style beats Blazor scoped CSS, so the width always wins over the
/// wrapper's default <c>width:100%</c>); the control sizes from that host via
/// the slotted-control rule in <c>SchoolCollab.Admin.Shared/wwwroot/app.css</c>.
/// The same pixel values are encoded as the <c>w-1</c>…<c>w-9</c> classes in
/// <c>src/SchoolCollab.Admin/wwwroot/css/app.css</c> for third-party inputs —
/// keep the two in sync.
/// </summary>
public static class FieldWidthExtensions
{
    /// <summary>
    /// Returns the inline CSS <c>style</c> string for <paramref name="width"/>,
    /// e.g. <c>"width:80px; min-width:0; max-width:100%"</c>. <c>min-width:0</c>
    /// lets the host honor widths below its intrinsic minimum;
    /// <c>max-width:100%</c> prevents a long value from blowing out its column.
    /// The string is applied to the <c>fluent-field</c> host — the slotted
    /// control is sized from it by the rule in
    /// <c>src/SchoolCollab.Admin.Shared/wwwroot/app.css</c>.
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
        // W9's flex basis must be `auto`, not `0` — see the remark on FieldWidth.W9.
        FieldWidth.W9 => "width:100%; min-width:0; max-width:100%; flex:1 1 auto",
        _ => throw new System.ArgumentOutOfRangeException(nameof(width), width, $"Unknown {nameof(FieldWidth)} value."),
    };
}