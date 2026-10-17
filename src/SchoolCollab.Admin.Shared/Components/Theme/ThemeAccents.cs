namespace SchoolCollab.Admin.Shared.Components.Theme;

/// <summary>
/// One entry of the theme accent picker: the display name of a v4 <c>OfficeColor</c> accent
/// and the brand hex that replaces it in v5.
/// </summary>
/// <param name="Name">Display name (the v4 <c>OfficeColor</c> member name).</param>
/// <param name="Color">Brand base colour as hex, in the shape
/// <c>IThemeService.SetThemeAsync(string, bool)</c> expects.</param>
public sealed record ThemeAccent(string Name, string Color);

/// <summary>
/// The accent palette, in one place so the picker and the one-time v4 migration cannot drift
/// apart.
/// </summary>
/// <remarks>
/// v5 removed the <c>OfficeColor</c> enum — the brand is simply a colour now — so the repo keeps
/// the six accents it offered, by name, as hex values. Each hex is that Office product's brand
/// colour, which is what <c>OfficeColor</c> selected in v4.
/// </remarks>
public static class ThemeAccents
{
    /// <summary>The six v4 <c>OfficeColor</c> accents as hex, in the picker's order.</summary>
    public static readonly ThemeAccent[] All =
    [
        new("Teams", "#6264A7"),
        new("Word", "#2B579A"),
        new("Excel", "#217346"),
        new("PowerPoint", "#D24726"),
        new("Outlook", "#0F6CBD"),
        new("OneNote", "#7719AA"),
    ];

    /// <summary>
    /// Resolves a value read from v4's theme store (<c>primaryColor</c>) to a brand hex: a value
    /// that already looks like a hex is passed through, and a v4 <c>OfficeColor</c> member name
    /// is mapped to its hex. Returns <see langword="null"/> for anything else, so a caller can
    /// leave the brand alone rather than guess.
    /// </summary>
    /// <remarks>
    /// v4's JS accepted either shape for <c>primary-color</c>
    /// (<c>t == null || !t.startsWith("#") ? getHexColor(t) : t</c>), so both must be handled here.
    /// </remarks>
    public static string? ResolveLegacyColor(string? legacyValue)
    {
        if (string.IsNullOrWhiteSpace(legacyValue))
        {
            return null;
        }

        var value = legacyValue.Trim();
        if (value.StartsWith('#'))
        {
            return value.ToUpperInvariant();
        }

        return All.FirstOrDefault(accent => string.Equals(accent.Name, value, StringComparison.OrdinalIgnoreCase))?.Color;
    }
}
