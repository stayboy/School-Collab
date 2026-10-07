namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>
/// Form model for <c>TargetsAndAudienceDialog</c> (grill decisions: the dialog asks for a category,
/// then shows ONLY the typed picker that applies to it — never all fields at once), where every
/// entity picker is SINGLE-value and APPENDS its pick to <see cref="PickedEntries"/>, the
/// dismissible chip zone above it (the <c>GuardianPickerDialog</c> precedent). A category change
/// clears <see cref="PickedEntries"/> first (<see cref="ClearPicked"/>), because the new category is a
/// different kind of target.
/// <para>The dialog owns no data source: the caller fills in the option lists for the three
/// list-backed categories and the student search delegate for the server-searched one.</para>
/// </summary>
public sealed class TargetsAndAudienceDialogModel
{
    /// <summary>The selected category. The dialog renders the matching value control.</summary>
    public TargetsAndAudienceCategory Category { get; set; } = TargetsAndAudienceCategory.GradeLevels;

    /// <summary>
    /// The entries picked so far, in pick order — one chip each, and the whole submission: the pickers
    /// append and reset to their placeholder rather than holding a selection, so a double pick must not
    /// mint a duplicate chip (deduped by <c>(Category, RefId)</c>).
    /// <para>Entity chips carry their own category, so <see cref="AppendPicked"/> itself accepts a
    /// mixed-kind batch; the dialog never produces one, because a category change empties the list
    /// (<see cref="ClearPicked"/>) before the new category applies. The single
    /// <see cref="TargetsAndAudienceCategory.Everyone"/> checkpoint never mixes with entity chips either,
    /// because each side clears the other (TGT-2).</para>
    /// </summary>
    public List<TargetsAndAudienceEntry> PickedEntries { get; } = [];

    /// <summary>The options the GradeLevels picker offers.</summary>
    public IReadOnlyList<TargetsAndAudienceOption> GradeLevelOptions { get; init; } = [];

    /// <summary>The options the Streams picker offers.</summary>
    public IReadOnlyList<TargetsAndAudienceOption> StreamOptions { get; init; } = [];

    /// <summary>The options the ActivityGroups picker offers.</summary>
    public IReadOnlyList<TargetsAndAudienceOption> ActivityGroupOptions { get; init; } = [];

    /// <summary>
    /// The server-side search behind the Students picker (TGT-6 — students are searched, never
    /// preloaded). Null when the caller supplies no roster source; the picker then answers an empty
    /// result set for every query instead of failing.
    /// </summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<TargetsAndAudienceOption>>>? StudentSearch { get; init; }

    /// <summary>
    /// Appends one picked value as a chip and resets the control (the caller clears its own displayed
    /// selection). Dedupes by <c>(Category, RefId)</c> — a double pick returns without minting a second
    /// chip — and clears the Everyone checkpoint first, because TGT-2 forbids the two mixing.
    /// <see cref="TargetsAndAudienceCategory.Everyone"/> routes to <see cref="PickEveryone"/>: that
    /// category's value is the value-less checkpoint itself.
    /// </summary>
    public void AppendPicked(TargetsAndAudienceCategory category, TargetsAndAudienceOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        if (category == TargetsAndAudienceCategory.Everyone)
        {
            PickEveryone();
            return;
        }

        var refId = Guid.Parse(option.Value);
        PickedEntries.RemoveAll(e => e.Category == TargetsAndAudienceCategory.Everyone);

        if (PickedEntries.Any(e => e.Category == category && e.RefId == refId))
        {
            return;
        }

        PickedEntries.Add(new TargetsAndAudienceEntry(category, refId, option.Label));
    }

    /// <summary>
    /// Empties the picked set. The dialog calls it when the author switches category: the new category
    /// is a different KIND of target, so the previous category's chips must not linger beside the new
    /// category's picker.
    /// </summary>
    public void ClearPicked() => PickedEntries.Clear();

    /// <summary>
    /// The Everyone checkpoint: the whole cohort and nothing else (TGT-2 mutual exclusion), so every
    /// entity chip is cleared. Idempotent — a second pick of the checkpoint does not duplicate it.
    /// </summary>
    public void PickEveryone()
    {
        PickedEntries.RemoveAll(e => e.Category != TargetsAndAudienceCategory.Everyone);

        if (PickedEntries.Count == 0)
        {
            PickedEntries.Add(new TargetsAndAudienceEntry(
                TargetsAndAudienceCategory.Everyone, null, TargetsAndAudienceEntry.EveryoneLabel));
        }
    }

    /// <summary>
    /// Dismisses one chip. Record VALUE equality is not enough here: two entries may legitimately be
    /// equal, so removing by value would drop the FIRST match when the second chip was clicked. The
    /// entry instance the rendered chip closed over is the one identity that names it.
    /// </summary>
    public void RemovePicked(TargetsAndAudienceEntry entry) =>
        PickedEntries.RemoveAll(e => ReferenceEquals(e, entry));
}
