namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>
/// The requested mode of the shared <c>AssignmentAuthoring</c> component
/// (assignment-authoring-compartments §5 / UX-9): <see cref="Create"/> (empty form, Save
/// creates a Draft), <see cref="Edit"/> (loaded form, full parity with Create while the
/// assignment is <c>Draft</c> or <c>Scheduled</c>) and <see cref="View"/> (all six
/// compartments read-only).
///
/// <para><see cref="Edit"/> degrades to <see cref="View"/> internally when the loaded
/// assignment is in a later status (<c>Published</c>, <c>Closed</c>, <c>Archived</c>), so a
/// host never has to test the status itself — the status→mode rule lives in exactly one
/// place (§11 field editability, UX-11).</para>
/// </summary>
public enum AssignmentAuthoringMode
{
    /// <summary>Empty form; the primary action creates a Draft assignment.</summary>
    Create = 0,

    /// <summary>Loaded form; editable while the assignment is Draft or Scheduled.</summary>
    Edit = 1,

    /// <summary>All compartments read-only; the surface for Published / Closed / Archived.</summary>
    View = 2
}
