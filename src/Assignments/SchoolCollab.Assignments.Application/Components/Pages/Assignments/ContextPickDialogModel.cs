namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>
/// Form model for <c>ContextPickDialog</c> (spec assignment-create-edit-redesign D4): the compact
/// strand/lesson picker that the Basics "Add strand" / "Add lesson" buttons open. The dialog is
/// data-free — the caller seeds <see cref="Options"/> (the subject's root strands, or the lessons the
/// pick source offers) and <see cref="Selected"/> (the picks already on the form model), so an OK is a
/// FULL REPLACEMENT of that pick set and a dangling pick (CP-11) the option list no longer resolves
/// survives as a seeded-but-unlisted selection rather than being silently dropped.
/// </summary>
public sealed class ContextPickDialogModel
{
    /// <summary>The noun the dialog titles its empty-state and checkbox list with ("strand"/"lesson").</summary>
    public string Noun { get; init; } = "strand";

    /// <summary>The pickable options — the page's already-loaded pick source, never fetched here.</summary>
    public IReadOnlyList<ContextPicksSection.ContextPickOption> Options { get; init; } = [];

    /// <summary>The text shown when <see cref="Options"/> is empty (the section's own CP-3 note).</summary>
    public string? EmptyText { get; init; }

    /// <summary>
    /// The current pick set, seeded by the caller and mutated by the dialog's checkboxes. A seeded id
    /// that <see cref="Options"/> does not list (a dangling pick) stays in this set unless the author
    /// removes it from the chip row — the dialog can only toggle what it can render.
    /// </summary>
    public HashSet<Guid> Selected { get; init; } = [];

    /// <summary>Whether one option is checked.</summary>
    public bool IsSelected(Guid id) => Selected.Contains(id);

    /// <summary>Checkbox callback: adds or removes one option id from the pick set.</summary>
    public void Set(Guid id, bool selected)
    {
        if (selected)
        {
            Selected.Add(id);
        }
        else
        {
            Selected.Remove(id);
        }
    }
}

/// <summary>The dialog's success payload: the full replacement pick set (pick order follows the
/// option order plus any dangling ids, which the caller re-derives if order matters — it does not,
/// the model stores an id list).</summary>
public sealed record ContextPickDialogResult(IReadOnlyList<Guid> Ids);
