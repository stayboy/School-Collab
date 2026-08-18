namespace SchoolCollab.Admin.Shared.Components;

/// <summary>
/// Layout orientation for <see cref="FormRow"/>. Lets callers pick a horizontal
/// label-on-the-left / input-on-the-right row or a vertical label-on-top /
/// input-below row explicitly, instead of relying on the auto
/// <c>@media (max-width: 720px)</c> fallback in <c>FormRow.razor.css</c> that
/// used to silently stack the row at any narrow viewport — including a 420px
/// side drawer hosted inside a wide desktop browser.
/// </summary>
/// <remarks>
/// <para><b>Why an enum and not a <c>bool Stacked</c>?</b> Two explicit values
/// keep the call site self-documenting
/// (<c>&lt;FormRow Orientation="Vertical"&gt;</c> reads as intent immediately)
/// and leave room to add a future <c>Responsive</c> alias if we ever want
/// "behave like the old auto-stack" as an opt-in rather than the default.</para>
/// <para><b>Why is the default <see cref="Horizontal"/>?</b> The horizontal
/// 180px-label + flex-input-cell layout is the canonical School-Collab form
/// pattern; every existing <c>&lt;FormRow&gt;</c> call site uses it and must
/// keep rendering identically. New callers wanting a stacked layout pass
/// <c>Orientation="Vertical"</c>.</para>
/// </remarks>
public enum RowOrientation
{
    /// <summary>
    /// Label sits in the 180px left column, input cell flex-fills the right.
    /// The canonical School-Collab form-row layout. This is the default.
    /// </summary>
    Horizontal,

    /// <summary>
    /// Label sits on top of its input cell, both full width. Use for narrow
    /// surfaces (side drawers, stacked mobile layouts) where the 180px
    /// horizontal column would consume too much of the row.
    /// </summary>
    Vertical,
}