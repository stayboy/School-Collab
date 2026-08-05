using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Settings.Application.Components.Pages.EntityCodeRules;

/// <summary>
/// Form-state and result types for the per-segment edit dialog
/// (<c>SegmentEditDialog</c>). Lives in a separate file (rather than inline
/// in the <c>.razor</c>) to keep the dialog markup focused and to make the
/// record/result types reachable from the grid and the Edit page without
/// re-importing the dialog file.
/// </summary>

/// <summary>
/// The dialog's form-state object — bound to the <c>SegmentFormModel</c>
/// passed in by the caller (the grid). The dialog mutates fields in place
/// so the grid sees the changes immediately; <see cref="OriginalIndex"/>
/// preserves the pre-edit index for the grid to find the right row
/// (indices can be re-ordered externally while the dialog is open).
/// </summary>
public sealed class SegmentEditFormModel
{
    /// <summary>The original segment index (used by the grid to find the row after the dialog closes).</summary>
    public int OriginalIndex { get; set; }

    /// <summary>Mutable copy of the segment's editable fields. The dialog binds to this directly.</summary>
    public SegmentFormModel Segment { get; set; } = default!;
}

/// <summary>
/// Result returned by <c>SegmentEditDialog.SubmitAsync</c>. Carries the
/// original row index (for the grid to locate the row) and the mutated
/// <see cref="SegmentFormModel"/> reference (the grid overwrites the row's
/// fields with this object's field values — NOT replacing the row, so the
/// grid's caller still holds the original reference).
/// </summary>
public sealed record SegmentEditResult(int OriginalIndex, SegmentFormModel Segment);