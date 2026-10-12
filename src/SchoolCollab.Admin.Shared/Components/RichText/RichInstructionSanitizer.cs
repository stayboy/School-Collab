using System.Text.RegularExpressions;
using Ganss.Xss;

namespace SchoolCollab.Admin.Shared.Components.RichText;

/// <summary>
/// The ONE sanitiser for authored instruction HTML (§8.4 Q11/Q15, round <c>wysiwyg-instruction-editor</c>):
/// allow-list only, applied on WRITE (before a value can reach the model) and again on RENDER (before it
/// becomes markup). Nothing else in the tree may turn a stored string into HTML.
///
/// <para>The allow-list is deliberately <b>tags-only</b> — no <c>style</c> attribute anywhere — which is
/// exactly why the editor's toolbar omits Quill's colour/background/font/size formats: they emit inline
/// styles, and the alternative would be loosening this policy to admit them. That trade-off is recorded
/// in the round doc (G4) rather than discovered later.</para>
///
/// <para>Plain text is untouched (Q16): legacy instructions keep rendering exactly as before. A bare
/// "&lt;" or "&gt;" is not markup, so the text survives — entity-encoded rather than dropped, which
/// renders character-for-character what the author typed.</para>
/// </summary>
public static class RichInstructionSanitizer
{
    /// <summary>The tags the restricted toolbar can actually produce — nothing else is admitted.</summary>
    private static readonly string[] AllowedTags =
        ["p", "br", "strong", "em", "u", "s", "ol", "ul", "li", "h1", "h2", "h3", "h4", "a"];

    /// <summary>
    /// Sanitises authored instruction HTML. Null or whitespace becomes <see cref="string.Empty"/> — this
    /// never returns null, so a caller can always render the result.
    /// </summary>
    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        return Create().Sanitize(html);
    }

    /// <summary>
    /// A display-side plain-text projection — NOT a security boundary (that is <see cref="Sanitize"/>):
    /// tags are dropped and entities decoded, so a one-line readout can show authored HTML as the words
    /// on the page rather than the markup, and an "empty" rich payload (Quill yields <c>&lt;p&gt;&lt;br&gt;&lt;/p&gt;</c>)
    /// can be recognised as empty. Suits readouts and emptiness checks — never a place that must decide
    /// what is SAFE.
    /// </summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        return System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", string.Empty)).Trim();
    }

    /// <summary>
    /// A fresh sanitiser per call: the Ganss instance is not documented as thread-safe, and instruction
    /// saves are not hot enough to justify caching one that is mutated elsewhere.
    /// </summary>
    private static HtmlSanitizer Create()
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        foreach (var tag in AllowedTags)
        {
            sanitizer.AllowedTags.Add(tag);
        }

        // A link is the only element that keeps an attribute, and only its target. No `style` anywhere —
        // see the class note: the toolbar cannot emit one, so admitting it would be surface with no job.
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedCssProperties.Clear();

        // http/https/mailto only, so `javascript:` and `data:` targets are dropped rather than trusted.
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");

        return sanitizer;
    }
}
