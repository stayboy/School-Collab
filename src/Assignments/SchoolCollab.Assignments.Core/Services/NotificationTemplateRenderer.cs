using System.Net;
using System.Text.RegularExpressions;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.RichText;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>The queue-time render outcome: the subject and body persisted on the
/// <see cref="Domain.NotificationLog"/> row plus the id of the template that rendered it
/// (<see langword="null"/> = built-in default — spec §3.6's audit semantics).</summary>
public sealed record RenderedMessage(string Subject, string Body, Guid? TemplateId);

/// <summary>
/// Pure static renderer replacing <see cref="NotificationMessageBuilder"/> <b>as the render
/// path</b> for the publish flow (round notification-templates-b1, spec §3.2/§3.3). The
/// builder survives as the <b>sole built-in default source</b> — this class delegates its
/// fallback to it and owns no texts of its own. It resolves nothing and owns no state: it
/// takes the (possibly null) resolved template plus the assembled
/// <see cref="NotificationTagValues"/> and produces the <see cref="RenderedMessage"/>.
///
/// <para>Tag semantics (§3.2): matching is case-insensitive and the retired Round-A
/// spellings resolve through <see cref="NotificationTemplateTags.Aliases"/>; an
/// <b>unknown tag stays literal</b> (visible in preview, never silently dropped); the
/// plain-text values (title, names, role, school, link, dates) are HTML-escaped in email
/// bodies and substituted plain in SMS/WhatsApp, while <c>{{Instructions}}</c> and
/// <c>{{Description}}</c> stay authored markup (email-only for instructions, stripped from
/// SMS/WhatsApp); the composed email body is re-sanitized with the ONE instruction
/// sanitizer (§3.12 — same allow-list the editor writes with).</para>
/// </summary>
public static partial class NotificationTemplateRenderer
{
    /// <summary>Matches <c>{{TagName}}</c> (optional inner whitespace) for the tag substitution pass.</summary>
    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9]+)\s*\}\}")]
    private static partial Regex TagPattern();

    /// <summary>
    /// Renders the message for <paramref name="kind"/> on <paramref name="channel"/>.
    /// <paramref name="template"/> <see langword="null"/> (no tenant template, or a failed
    /// Settings read) renders the built-in defaults with <see cref="RenderedMessage.TemplateId"/>
    /// <see langword="null"/>.
    /// </summary>
    public static RenderedMessage Render(
        NotificationKind kind,
        NotificationChannel channel,
        ResolvedNotificationTemplate? template,
        NotificationTagValues tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        return channel == NotificationChannel.Email
            ? RenderEmail(kind, template, tags)
            : RenderSms(kind, template, tags);
    }

    private static RenderedMessage RenderEmail(
        NotificationKind kind,
        ResolvedNotificationTemplate? template,
        NotificationTagValues tags)
    {
        if (template is null)
        {
            return new RenderedMessage(
                NotificationMessageBuilder.SubjectFor(kind, tags.AssignmentTitle),
                NotificationMessageBuilder.BuildEmailBody(kind, tags),
                TemplateId: null);
        }

        // Plain-text values are HTML-escaped for the email body (§3.2); {{Instructions}} and
        // {{Description}} expand AS authored markup (both are sanitized rich HTML). The composed
        // body is re-sanitized either way (§3.12: one sanitizer, two applications).
        var body = Substitute(
            template.Body,
            tags,
            instructionsValue: tags.Instructions ?? string.Empty,
            allowInstructions: true,
            escapeValues: true);

        // The subject is a plain-text header line (MailKit encodes it on the wire), never HTML —
        // so it takes the raw substitution: a title containing '&' must read '&', not '&amp;'.
        var subject = template.Subject is null
            ? NotificationMessageBuilder.SubjectFor(kind, tags.AssignmentTitle)
            : Substitute(
                template.Subject,
                tags,
                instructionsValue: string.Empty,
                allowInstructions: false,
                escapeValues: false);

        return new RenderedMessage(subject, RichInstructionSanitizer.Sanitize(body), template.TemplateId);
    }

    private static RenderedMessage RenderSms(
        NotificationKind kind,
        ResolvedNotificationTemplate? template,
        NotificationTagValues tags)
    {
        // §5: the subject is ignored for SMS — the kind's built-in line is what lands in
        // the persisted row regardless of any authored template subject.
        var subject = NotificationMessageBuilder.SubjectFor(kind, tags.AssignmentTitle);

        if (template is null)
        {
            return new RenderedMessage(
                subject, NotificationMessageBuilder.BuildSmsBody(kind, tags), TemplateId: null);
        }

        // Plain-text channel: values substitute raw (no HTML escaping) and {{Instructions}} is
        // stripped entirely (email-only per §3.2) — the tag disappears rather than staying literal.
        var body = Substitute(
            template.Body,
            tags,
            instructionsValue: string.Empty,
            allowInstructions: false,
            escapeValues: false);

        body = StripInstructionsTags(body);

        return new RenderedMessage(subject, body, template.TemplateId);
    }

    /// <summary>
    /// Substitutes <c>{{Tag}}</c> occurrences. <paramref name="escapeValues"/> is the channel's
    /// escape decision: <see langword="true"/> for an email body (plain-text values become HTML
    /// entities), <see langword="false"/> for SMS/WhatsApp and for the plain-text subject line.
    /// Unknown tags are returned <b>literal</b>, never dropped.
    /// </summary>
    private static string Substitute(
        string input,
        NotificationTagValues tags,
        string instructionsValue,
        bool allowInstructions,
        bool escapeValues)
    {
        return TagPattern().Replace(input, match =>
        {
            var name = match.Groups[1].Value;
            return ResolveTag(name, tags, instructionsValue, allowInstructions, escapeValues) ?? match.Value;
        });
    }

    /// <summary>The resolved value for a known tag, or <see langword="null"/> to keep the literal.</summary>
    private static string? ResolveTag(
        string name,
        NotificationTagValues tags,
        string instructionsValue,
        bool allowInstructions,
        bool escapeValues)
    {
        string Escape(string value) => escapeValues ? WebUtility.HtmlEncode(value) : value;

        switch (NotificationTemplateTags.Canonicalize(name))
        {
            case NotificationTemplateTags.Title:
                return Escape(tags.AssignmentTitle);
            case NotificationTemplateTags.Description:
                // Full description — `Assignment.Description` is the plain-text author summary, so it
                // is escaped like the other plain-text values (the builder encodes its excerpt the
                // same way). The 120-char cap belongs to the built-in preview body only
                // (`NotificationMessageBuilder.BuildEmailBody`); `{{Instructions}}` is the one tag
                // that expands as authored rich markup.
                return Escape(tags.Description ?? string.Empty);
            case NotificationTemplateTags.DueDate:
                return tags.DueDate is null ? string.Empty : Escape(FormatDate(tags.DueDate.Value));
            case NotificationTemplateTags.Instructions:
                // Email only. In an SMS body the caller strips the tag afterwards (§3.2:
                // stripped, not literal), so returning null here keeps email bodies correct.
                return allowInstructions ? instructionsValue : null;
            case NotificationTemplateTags.StudentName:
                return Escape(tags.StudentName);
            case NotificationTemplateTags.GuardianName:
                return Escape(tags.ContactName);
            case NotificationTemplateTags.GuardianRole:
                return Escape(tags.GuardianRole);
            case NotificationTemplateTags.SchoolName:
                return Escape(tags.SchoolName);
            case NotificationTemplateTags.Link:
                return Escape(tags.Link);
            case NotificationTemplateTags.LinkExpiry:
                return tags.LinkExpiry is null ? string.Empty : Escape(FormatDate(tags.LinkExpiry.Value));
            default:
                return null; // unknown tag — stays literal (§3.2)
        }
    }

    /// <summary>Removes <c>{{Instructions}}</c> from an SMS body entirely (§3.2: stripped,
    /// not literal) after the substitution pass left it untouched.</summary>
    internal static string StripInstructionsTags(string body) =>
        TagPattern().Replace(
            body,
            m => m.Groups[1].Value.Equals(NotificationTemplateTags.Instructions, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : m.Value);

    /// <summary>Short invariant rendering used by <c>{{DueDate}}</c>, <c>{{LinkExpiry}}</c> and the
    /// built-in preview body's due-date line.</summary>
    internal static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
}
