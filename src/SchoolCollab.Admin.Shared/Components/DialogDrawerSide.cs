namespace SchoolCollab.Admin.Shared.Components;

/// <summary>Which side of the dialog content area the
/// <see cref="DialogDrawer"/> panel anchors to. The drawer fills the full
/// dialog body content area (between the title bar and the actions bar)
/// and slides in from the chosen edge.</summary>
public enum DialogDrawerSide
{
    /// <summary>Panel anchored to the right edge of the dialog body. The
    /// default — matches the operator's reading direction (left-to-right
    /// languages) and leaves the main form visible on the left.</summary>
    Right,

    /// <summary>Panel anchored to the left edge of the dialog body.</summary>
    Left,
}