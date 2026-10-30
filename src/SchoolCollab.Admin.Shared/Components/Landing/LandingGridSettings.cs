using Microsoft.FluentUI.AspNetCore.Components;

namespace SchoolCollab.Admin.Shared.Components.Landing;

/// <summary>
/// Configuration the <see cref="LandingPage{TItem}"/> wrapper applies to its
/// owned <c>&lt;FluentDataGrid&gt;</c>. Pages supply an instance (typically a
/// <c>static readonly</c> field) so the grid's layout lives with the page while
/// the wrapper owns the grid element itself.
/// </summary>
public sealed class LandingGridSettings
{
    /// <summary>
    /// CSS grid template for the data grid columns, e.g.
    /// <c>"minmax(180px,2fr) 1fr 1fr auto"</c>. Required — there is no useful
    /// default because every landing page has a different column count/shape.
    /// </summary>
    public string GridTemplateColumns { get; set; } = string.Empty;

    /// <summary>Header generation. Defaults to <c>Sticky</c>.</summary>
    public DataGridGeneratedHeaderType GenerateHeader { get; set; } = DataGridGeneratedHeaderType.Sticky;

    /// <summary>Whether cells wrap onto multiple lines. Defaults to <c>true</c>.</summary>
    public bool MultiLine { get; set; } = true;

    /// <summary>Number of items to display per page. Defaults to 15 —
    /// `.github/copilot/rules/blazor-components.md` § "Grids are paged by default". 15 is the repo default, not a
    /// policy: a grid declares its own when its row density differs.</summary>
    public int ItemsPerPage { get; set; } = 15;
}