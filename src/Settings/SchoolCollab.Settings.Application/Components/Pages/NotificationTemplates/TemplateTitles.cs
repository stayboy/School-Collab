using SchoolCollab.Core.RichText;

namespace SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

/// <summary>
/// The one place a template's identity label is derived (naming spec D3): the operator's <b>title</b>.
/// <para>
/// The pre-naming chain — the subject, then the first ~80 characters of the body's plain text — is kept
/// only as a last resort, so a row that predates the title column (or a payload that omits it) still
/// renders something rather than an empty string. Nothing else may read the subject to identify a row.
/// </para>
/// </summary>
internal static class TemplateTitles
{
    /// <summary>The label for a template: title, else subject, else a body excerpt, else the fallback.</summary>
    /// <param name="title">The template's title — the only true identity.</param>
    /// <param name="subject">The email subject line (content, not a name).</param>
    /// <param name="body">The authored body (HTML or plain text) — excerpted only when both above are blank.</param>
    /// <param name="fallback">What to render when every content field is blank; callers pass the
    /// kind × contact-mode label so the result is never an empty string.</param>
    public static string For(string? title, string? subject, string? body, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();

        if (!string.IsNullOrWhiteSpace(subject))
            return subject.Trim();

        var plain = RichInstructionSanitizer.ToPlainText(body ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(plain))
            return plain.Length <= 80 ? plain : plain[..80] + "...";

        return fallback;
    }
}
