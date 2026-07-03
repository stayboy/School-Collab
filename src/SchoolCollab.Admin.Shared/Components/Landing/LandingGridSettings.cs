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
    public GenerateHeaderOption GenerateHeader { get; set; } = GenerateHeaderOption.Sticky;

    /// <summary>Whether cells wrap onto multiple lines. Defaults to <c>true</c>.</summary>
    public bool MultiLine { get; set; } = true;
}