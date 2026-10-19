using System.Net;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.RichText;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// The <b>single source of the built-in notification texts</b> — subject line and default
/// body per <see cref="NotificationKind"/> × channel (spec §3.3). Kept static and
/// transport-free so the rendered subject / body / deep link are unit-testable, and so the
/// broadcaster persists exactly what the dispatcher later sends (decision (b)).
///
/// <para>Round notification-templates-b1: the default bodies were upgraded to the §3.3
/// preview shape (bold title, due date, one-line description excerpt, then the link
/// anchor) and the <see cref="NotificationTemplateRenderer"/> now delegates its built-in
/// fallback here — one implementation, so the publish path and the E3 sweeps can never
/// drift apart. The tag-values overloads take the renderer's
/// <see cref="NotificationTagValues"/>; the per-kind overloads are the narrow entry points
/// the sweeps still use and delegate to them.</para>
/// </summary>
public static class NotificationMessageBuilder
{
    /// <summary>The contact-scoped deep-link path embedded in every published message.</summary>
    public const string DeepLinkPathPrefix = "/deeplink/";

    /// <summary>The description excerpt cap — the built-in preview body renders a one-line summary.</summary>
    private const int DescriptionExcerptLength = 120;

    /// <summary>Renders the built-in default email message for <paramref name="kind"/>.</summary>
    public static EmailMessage BuildEmail(NotificationKind kind, string toAddress, NotificationTagValues tags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentNullException.ThrowIfNull(tags);

        return new EmailMessage(toAddress, SubjectFor(kind, tags.AssignmentTitle), BuildEmailBody(kind, tags));
    }

    /// <summary>Renders the built-in default SMS / WhatsApp body for <paramref name="kind"/>.</summary>
    public static SmsMessage BuildSms(NotificationKind kind, string toAddress, NotificationTagValues tags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentNullException.ThrowIfNull(tags);

        return new SmsMessage(toAddress, BuildSmsBody(kind, tags));
    }

    /// <summary>
    /// The built-in default email body for <paramref name="kind"/> (§3.3 preview shape): a
    /// kind-specific intro with the bold title, the due date and capped description excerpt
    /// when the assignment carries them, then the deep-link paragraph. Sanitized with the
    /// ONE instruction sanitizer (§3.12) — it is markup, and markup leaves this class safe.
    /// </summary>
    public static string BuildEmailBody(NotificationKind kind, NotificationTagValues tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var title = WebUtility.HtmlEncode(tags.AssignmentTitle);
        var intro = kind switch
        {
            NotificationKind.Reminder =>
                $"<p>This is a reminder for the assignment <strong>{title}</strong>.</p>",
            NotificationKind.Completion =>
                $"<p><strong>{title}</strong> is complete and ready for your signature.</p>",
            NotificationKind.Overdue =>
                $"<p>The assignment <strong>{title}</strong> is now overdue.</p>",
            _ =>
                $"<p>A new assignment <strong>{title}</strong> has been published.</p>",
        };

        var details = string.Empty;
        if (tags.DueDate is not null)
        {
            details += $"<p>Due: {WebUtility.HtmlEncode(NotificationTemplateRenderer.FormatDate(tags.DueDate.Value))}</p>";
        }

        var excerpt = Excerpt(tags.Description);
        if (excerpt.Length > 0)
        {
            details += $"<p>{WebUtility.HtmlEncode(excerpt)}</p>";
        }

        var linkLabel = kind == NotificationKind.Completion ? "Review and sign" : "View the assignment";
        var link = tags.Link.Length == 0
            ? string.Empty
            : $"<p><a href=\"{WebUtility.HtmlEncode(tags.Link)}\">{linkLabel}</a></p>";

        return RichInstructionSanitizer.Sanitize(intro + details + link);
    }

    /// <summary>
    /// The built-in default SMS / WhatsApp body for <paramref name="kind"/> (plain text):
    /// the kind's subject line plus the channel-appropriate link.
    /// </summary>
    public static string BuildSmsBody(NotificationKind kind, NotificationTagValues tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var action = kind == NotificationKind.Completion ? "Sign" : "Open";

        return tags.Link.Length == 0
            ? SubjectFor(kind, tags.AssignmentTitle)
            : $"{SubjectFor(kind, tags.AssignmentTitle)} {action}: {tags.Link}";
    }

    /// <summary>The capped single-line description excerpt; empty when there is no description.</summary>
    private static string Excerpt(string? description)
    {
        var plain = description is null ? string.Empty : description.Trim();
        if (plain.Length <= DescriptionExcerptLength)
        {
            return plain;
        }

        return string.Concat(plain.AsSpan(0, DescriptionExcerptLength), "…");
    }

    /// <summary>Returns the subject line for a kind (plain text — never HTML-escaped: a
    /// subject is a header line, not markup).</summary>
    public static string SubjectFor(NotificationKind kind, string assignmentTitle) => kind switch
    {
        NotificationKind.Reminder => BuildReminderSubject(assignmentTitle),
        NotificationKind.Completion => BuildCompletionSubject(assignmentTitle),
        NotificationKind.Overdue => BuildOverdueSubject(assignmentTitle),
        _ => BuildPublishSubject(assignmentTitle),
    };

    /// <summary>Renders the consolidated per-contact publish email.</summary>
    public static EmailMessage BuildPublishEmail(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);
        return BuildEmail(NotificationKind.Publish, toAddress, LegacyTags(assignmentTitle, deepLinkToken));
    }

    /// <summary>Renders the publish SMS / WhatsApp body (plain text).</summary>
    public static SmsMessage BuildPublishSms(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);
        return BuildSms(NotificationKind.Publish, toAddress, LegacyTags(assignmentTitle, deepLinkToken));
    }

    /// <summary>The published-notification subject line.</summary>
    public static string BuildPublishSubject(string assignmentTitle) => $"New assignment: {assignmentTitle}";

    /// <summary>Renders the E3 reminder email for a recipient due a nudge.</summary>
    public static EmailMessage BuildReminderEmail(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);
        return BuildEmail(NotificationKind.Reminder, toAddress, LegacyTags(assignmentTitle, deepLinkToken));
    }

    /// <summary>Renders the E3 reminder SMS / WhatsApp body (plain text).</summary>
    public static SmsMessage BuildReminderSms(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);
        return BuildSms(NotificationKind.Reminder, toAddress, LegacyTags(assignmentTitle, deepLinkToken));
    }

    /// <summary>The reminder notification subject line.</summary>
    public static string BuildReminderSubject(string assignmentTitle) => $"Reminder: {assignmentTitle}";

    /// <summary>Renders the E3 completion email to the signing guardian contact.</summary>
    public static EmailMessage BuildCompletionEmail(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);
        return BuildEmail(NotificationKind.Completion, toAddress, LegacyTags(assignmentTitle, deepLinkToken));
    }

    /// <summary>Renders the E3 completion SMS / WhatsApp body (plain text).</summary>
    public static SmsMessage BuildCompletionSms(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);
        return BuildSms(NotificationKind.Completion, toAddress, LegacyTags(assignmentTitle, deepLinkToken));
    }

    /// <summary>The completion-to-guardian subject line.</summary>
    public static string BuildCompletionSubject(string assignmentTitle) => $"Ready to sign: {assignmentTitle}";

    /// <summary>Renders the E3 overdue email for a recipient past due.</summary>
    public static EmailMessage BuildOverdueEmail(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);
        return BuildEmail(NotificationKind.Overdue, toAddress, LegacyTags(assignmentTitle, deepLinkToken));
    }

    /// <summary>Renders the E3 overdue SMS / WhatsApp body (plain text).</summary>
    public static SmsMessage BuildOverdueSms(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);
        return BuildSms(NotificationKind.Overdue, toAddress, LegacyTags(assignmentTitle, deepLinkToken));
    }

    /// <summary>The overdue notification subject line.</summary>
    public static string BuildOverdueSubject(string assignmentTitle) => $"Overdue: {assignmentTitle}";

    /// <summary>The tag values a title + deep-link token pair implies — the narrow sweeps'
    /// entry point into the same default texts (no enrichment, no due date, no description).</summary>
    private static NotificationTagValues LegacyTags(string assignmentTitle, string deepLinkToken) => new()
    {
        AssignmentTitle = assignmentTitle,
        Link = string.Concat(DeepLinkPathPrefix, deepLinkToken),
    };
}
