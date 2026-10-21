using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

/// <summary>One pickable constraint value: the stored encoding plus its author-facing name.</summary>
public sealed record ConstraintOption(string Value, string Text);

/// <summary>
/// The add-constraint dialog''s state (redesign spec §3.6): the dimension is PRESET by the editor''s kebab
/// item, so the operator never answers "which dimension" twice — the Targets-&amp;-Audience precedent
/// (`CategoryLocked`). Picks accumulate here and are only returned on submit; nothing is persisted until
/// the editor''s Save.
/// </summary>
public sealed class ConstraintEditModel
{
    /// <summary>The dimension this dialog adds values for (fixed for the visit).</summary>
    public TemplateConstraintDimension Dimension { get; init; }

    /// <summary>Every value of the dimension, already loaded and display-named.</summary>
    public IReadOnlyList<ConstraintOption> Options { get; init; } = [];

    /// <summary>Values the draft already carries, so a re-pick is a no-op instead of a duplicate.</summary>
    public IReadOnlyList<string> ExistingValues { get; init; } = [];

    /// <summary>The template the values are being added to (shown above the picker).</summary>
    public string TemplateLabel { get; init; } = "This template";

    /// <summary>The picks made in this visit, in order.</summary>
    public List<ConstraintOption> Picked { get; } = [];

    /// <summary>Options that are not picked yet — the picker''s list, reassigned on every pick.</summary>
    public ConstraintOption[] Available =>
        Options.Where(o => !IsPicked(o.Value) && !IsExisting(o.Value)).ToArray();

    /// <summary>Whether the value is already picked in this visit.</summary>
    public bool IsPicked(string value) =>
        Picked.Any(p => string.Equals(p.Value, value, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the draft already carries the value.</summary>
    public bool IsExisting(string value) =>
        ExistingValues.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));

    /// <summary>Appends a pick (no duplicates, existing values ignored).</summary>
    public void Append(ConstraintOption option)
    {
        if (IsPicked(option.Value) || IsExisting(option.Value))
            return;

        Picked.Add(option);
    }

    /// <summary>Drops one pick made in this visit.</summary>
    public void Remove(ConstraintOption option) => Picked.Remove(option);
}

/// <summary>The dialog''s result: the picked values (the dimension is the caller''s own).</summary>
public sealed record ConstraintEditResult(IReadOnlyList<string> Values);