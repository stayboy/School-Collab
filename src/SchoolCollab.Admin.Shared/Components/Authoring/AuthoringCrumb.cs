using Microsoft.FluentUI.AspNetCore.Components;

namespace SchoolCollab.Admin.Shared.Components.Authoring;

/// <summary>
/// One entry of an <see cref="AuthoringShell"/>'s breadcrumb trail
/// (`documents/solution/adr-authoring-surface-pattern.md`, decision 1). A null/empty
/// <paramref name="Href"/> renders plain text instead of a link — the assignment surface's loading
/// branch (`Assignments › …`) relies on that.
/// </summary>
/// <param name="Label">The crumb text.</param>
/// <param name="Href">The crumb's address, or null for a non-link crumb.</param>
public sealed record AuthoringCrumb(string Label, string? Href = null);
