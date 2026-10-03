using Microsoft.FluentUI.AspNetCore.Components;

namespace SchoolCollab.Admin.Shared.Components;

/// <summary>
/// Describes a single action rendered inside a <see cref="RowActionsMenu"/> kebab
/// menu. Use the static factory methods (<see cref="Navigate"/>,
/// <see cref="Callback"/>, <see cref="Separator"/>) for the common cases.
/// </summary>
public sealed class RowAction
{
    /// <summary>Text shown for the menu item. Ignored when <see cref="IsSeparator"/> is true.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Optional leading icon (e.g. a constant from <see cref="Constants.FluentIcons"/>).</summary>
    public Icon? Icon { get; init; }

    /// <summary>
    /// Route to navigate to when the item is clicked. When both <see cref="Href"/>
    /// and <see cref="OnClick"/> are set, <see cref="Href"/> takes precedence.
    /// Ignored when <see cref="IsSeparator"/> is true.
    /// </summary>
    public string? Href { get; init; }

    /// <summary>
    /// Async callback invoked when the item is clicked. Mutually exclusive with
    /// <see cref="Href"/>. Ignored when <see cref="IsSeparator"/> is true.
    /// </summary>
    public Func<Task>? OnClick { get; init; }

    /// <summary>When true, the item is greyed out and non-clickable.</summary>
    public bool Disabled { get; init; }

    /// <summary>
    /// F7: WHY the action is disabled — rendered as the item's accessible description (its
    /// <c>title</c>) on both the single-action button and the kebab item, so a greyed-out action
    /// explains itself instead of being a dead end. Null (the default) renders no description at
    /// all: an action that cannot name a reason must not invent one, and every existing caller keeps
    /// its current rendering. Additive on purpose — <c>init</c>-only with a null default.
    /// </summary>
    public string? DisabledReason { get; init; }

    /// <summary>When true, renders a horizontal divider instead of a menu item.</summary>
    public bool IsSeparator { get; init; }

    /// <summary>
    /// When true, the action is destructive (e.g. Remove / Delete) and the
    /// <see cref="RowActionsMenu"/> shows a confirmation prompt before invoking
    /// it. Enforced at the component level so every destructive row action is
    /// gated by a user confirmation.
    /// </summary>
    public bool Destructive { get; init; }

    /// <summary>
    /// Optional custom confirmation message. When null, the menu falls back to a
    /// generic "Are you sure you want to {label}?" prompt.
    /// </summary>
    public string? ConfirmMessage { get; init; }

    // ── Factory helpers ──────────────────────────────────────────────────
    //
    // Optional parameters are APPENDED, never inserted: a new one placed beside `disabled` would
    // silently shift every positional `destructive` / `confirmMessage` argument already passing
    // through these factories, and this type has 22 call sites across Assignments, Settings and
    // Students.

    /// <summary>A menu item that navigates to <paramref name="href"/>.</summary>
    public static RowAction Navigate(
        string label, string href, Icon? icon = null, bool disabled = false, string? disabledReason = null) =>
        new() { Label = label, Href = href, Icon = icon, Disabled = disabled, DisabledReason = disabledReason };

    /// <summary>A menu item that invokes the async <paramref name="onClick"/> callback.</summary>
    public static RowAction Callback(string label, Func<Task> onClick, Icon? icon = null, bool disabled = false, bool destructive = false, string? confirmMessage = null, string? disabledReason = null) =>
        new() { Label = label, OnClick = onClick, Icon = icon, Disabled = disabled, Destructive = destructive, ConfirmMessage = confirmMessage, DisabledReason = disabledReason };

    /// <summary>A menu item that invokes the synchronous <paramref name="onClick"/> callback.</summary>
    public static RowAction Callback(string label, Action onClick, Icon? icon = null, bool disabled = false, bool destructive = false, string? confirmMessage = null, string? disabledReason = null) =>
        new() { Label = label, OnClick = () => { onClick(); return Task.CompletedTask; }, Icon = icon, Disabled = disabled, Destructive = destructive, ConfirmMessage = confirmMessage, DisabledReason = disabledReason };

    /// <summary>A horizontal divider between groups of actions.</summary>
    public static RowAction Separator() =>
        new() { IsSeparator = true };
}
