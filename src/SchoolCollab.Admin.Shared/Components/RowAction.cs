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

    /// <summary>When true, renders a horizontal divider instead of a menu item.</summary>
    public bool IsSeparator { get; init; }

    // ── Factory helpers ──────────────────────────────────────────────────

    /// <summary>A menu item that navigates to <paramref name="href"/>.</summary>
    public static RowAction Navigate(string label, string href, Icon? icon = null, bool disabled = false) =>
        new() { Label = label, Href = href, Icon = icon, Disabled = disabled };

    /// <summary>A menu item that invokes the async <paramref name="onClick"/> callback.</summary>
    public static RowAction Callback(string label, Func<Task> onClick, Icon? icon = null, bool disabled = false) =>
        new() { Label = label, OnClick = onClick, Icon = icon, Disabled = disabled };

    /// <summary>A menu item that invokes the synchronous <paramref name="onClick"/> callback.</summary>
    public static RowAction Callback(string label, Action onClick, Icon? icon = null, bool disabled = false) =>
        new() { Label = label, OnClick = () => { onClick(); return Task.CompletedTask; }, Icon = icon, Disabled = disabled };

    /// <summary>A horizontal divider between groups of actions.</summary>
    public static RowAction Separator() =>
        new() { IsSeparator = true };
}
