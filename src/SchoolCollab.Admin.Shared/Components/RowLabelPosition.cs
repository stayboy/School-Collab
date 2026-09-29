namespace SchoolCollab.Admin.Shared.Components;

/// <summary>
/// Where a <see cref="FormRow"/>'s label sits relative to its input cell, when that has to
/// be said explicitly.
/// </summary>
/// <remarks>
/// <para><b>Why only two members?</b> <see cref="RowOrientation"/> already owns the
/// canonical placements — label-left for <see cref="RowOrientation.Horizontal"/>,
/// label-above for <see cref="RowOrientation.Vertical"/> — and re-stating them here would
/// create two sources of truth that can silently disagree. <see cref="Default"/> therefore
/// means "whatever the row's orientation decides", and it is the value every existing
/// <c>&lt;FormRow&gt;</c> gets, which is why adding this parameter changed no rendered output
/// anywhere else in the repo. <see cref="Below"/> is the one placement the orientation
/// enum cannot express, and the only reason this enum exists.</para>
/// <para><b>Why an enum and not a <c>bool LabelBelow</c>?</b> Same reason as
/// <see cref="RowOrientation"/>: the call site reads as intent
/// (<c>&lt;FormRow LabelPosition="RowLabelPosition.Below"&gt;</c>) and a future placement can
/// be added without a second boolean that contradicts the first.</para>
/// <para><b>What "below" costs the caller.</b> In the horizontal layout the label column
/// is a fixed 180px and it is that constant width — not the input — that lines every input
/// up down a form. With the label beneath its input there is no label column left, so a
/// form mixing both placements loses that axis and has to align on input height plus
/// label line-height instead. Use it deliberately, for the surface it was added for, not
/// as a drop-in alternative to <see cref="RowOrientation.Vertical"/>.</para>
/// </remarks>
public enum RowLabelPosition
{
    /// <summary>
    /// The row's <see cref="RowOrientation"/> decides: label-left for
    /// <see cref="RowOrientation.Horizontal"/>, label-above for
    /// <see cref="RowOrientation.Vertical"/>. Every <c>&lt;FormRow&gt;</c> that does not pass
    /// this parameter keeps exactly the layout it had before the parameter existed.
    /// </summary>
    Default,

    /// <summary>
    /// The label sits <b>beneath</b> the input cell, both full width. Renders the input
    /// cell first and the label second — in the DOM as well as visually — so reading order
    /// and screen-reader order stay the same as the eye's. The <c>&lt;label for&gt;</c>
    /// association is unchanged, so clicking the label still focuses its input.
    /// </summary>
    Below,
}
