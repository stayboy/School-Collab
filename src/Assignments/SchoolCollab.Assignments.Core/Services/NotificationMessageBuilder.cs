using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services.Delivery;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Pure renderer for notification payloads. Kept static and transport-free so the
/// rendered subject / body / deep link are unit-testable, and so the broadcaster
/// persists exactly what the dispatcher later sends (decision (b)).
/// </summary>
public static class NotificationMessageBuilder
{
    /// <summary>The contact-scoped deep-link path embedded in every published message.</summary>
    public const string DeepLinkPathPrefix = "/deeplink/";

    /// <summary>Renders the consolidated per-contact publish email.</summary>
    public static EmailMessage BuildPublishEmail(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);

        return new EmailMessage(toAddress, BuildPublishSubject(assignmentTitle), BuildPublishHtml(assignmentTitle, deepLinkToken));
    }

    /// <summary>Renders the publish SMS / WhatsApp body (plain text).</summary>
    public static SmsMessage BuildPublishSms(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);

        return new SmsMessage(
            toAddress,
            $"{BuildPublishSubject(assignmentTitle)} Open: {DeepLinkPathPrefix}{deepLinkToken}");
    }

    /// <summary>The published-notification subject line.</summary>
    public static string BuildPublishSubject(string assignmentTitle) => $"New assignment: {assignmentTitle}";

    /// <summary>The published-notification HTML body carrying this contact's deep link.</summary>
    public static string BuildPublishHtml(string assignmentTitle, string deepLinkToken) =>
        $"<p>A new assignment <strong>{assignmentTitle}</strong> has been published.</p>" +
        $"<p><a href=\"{DeepLinkPathPrefix}{deepLinkToken}\">View the assignment</a></p>";

    /// <summary>Renders the E3 reminder email for a recipient due a nudge.</summary>
    public static EmailMessage BuildReminderEmail(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);
        return new EmailMessage(toAddress, BuildReminderSubject(assignmentTitle), BuildReminderHtml(assignmentTitle, deepLinkToken));
    }

    /// <summary>Renders the E3 reminder SMS / WhatsApp body (plain text).</summary>
    public static SmsMessage BuildReminderSms(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);
        return new SmsMessage(toAddress, $"{BuildReminderSubject(assignmentTitle)} Open: {DeepLinkPathPrefix}{deepLinkToken}");
    }

    /// <summary>The reminder notification subject line.</summary>
    public static string BuildReminderSubject(string assignmentTitle) => $"Reminder: {assignmentTitle}";

    /// <summary>The reminder HTML body carrying this contact's deep link.</summary>
    public static string BuildReminderHtml(string assignmentTitle, string deepLinkToken) =>
        $"<p>This is a reminder for the assignment <strong>{assignmentTitle}</strong>.</p>" +
        $"<p><a href=\"{DeepLinkPathPrefix}{deepLinkToken}\">View the assignment</a></p>";

    /// <summary>Renders the E3 completion email to the signing guardian contact.</summary>
    public static EmailMessage BuildCompletionEmail(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);
        return new EmailMessage(toAddress, BuildCompletionSubject(assignmentTitle), BuildCompletionHtml(assignmentTitle, deepLinkToken));
    }

    /// <summary>Renders the E3 completion SMS / WhatsApp body (plain text).</summary>
    public static SmsMessage BuildCompletionSms(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);
        return new SmsMessage(toAddress, $"{BuildCompletionSubject(assignmentTitle)} Sign: {DeepLinkPathPrefix}{deepLinkToken}");
    }

    /// <summary>The completion-to-guardian subject line.</summary>
    public static string BuildCompletionSubject(string assignmentTitle) => $"Ready to sign: {assignmentTitle}";

    /// <summary>The completion-to-guardian HTML body.</summary>
    public static string BuildCompletionHtml(string assignmentTitle, string deepLinkToken) =>
        $"<p><strong>{assignmentTitle}</strong> is complete and ready for your signature.</p>" +
        $"<p><a href=\"{DeepLinkPathPrefix}{deepLinkToken}\">Review and sign</a></p>";

    /// <summary>Renders the E3 overdue email for a recipient past due.</summary>
    public static EmailMessage BuildOverdueEmail(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);
        return new EmailMessage(toAddress, BuildOverdueSubject(assignmentTitle), BuildOverdueHtml(assignmentTitle, deepLinkToken));
    }

    /// <summary>Renders the E3 overdue SMS / WhatsApp body (plain text).</summary>
    public static SmsMessage BuildOverdueSms(string toAddress, string assignmentTitle, string deepLinkToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkToken);
        return new SmsMessage(toAddress, $"{BuildOverdueSubject(assignmentTitle)} Open: {DeepLinkPathPrefix}{deepLinkToken}");
    }

    /// <summary>The overdue notification subject line.</summary>
    public static string BuildOverdueSubject(string assignmentTitle) => $"Overdue: {assignmentTitle}";

    /// <summary>The overdue HTML body carrying this contact's deep link.</summary>
    public static string BuildOverdueHtml(string assignmentTitle, string deepLinkToken) =>
        $"<p>The assignment <strong>{assignmentTitle}</strong> is now overdue.</p>" +
        $"<p><a href=\"{DeepLinkPathPrefix}{deepLinkToken}\">View the assignment</a></p>";

    /// <summary>Returns the subject line for an E3 sweep kind (used by the queue-time
    /// skip rows that carry no rendered body).</summary>
    public static string SubjectFor(NotificationKind kind, string assignmentTitle) => kind switch
    {
        NotificationKind.Reminder => BuildReminderSubject(assignmentTitle),
        NotificationKind.Completion => BuildCompletionSubject(assignmentTitle),
        NotificationKind.Overdue => BuildOverdueSubject(assignmentTitle),
        _ => BuildPublishSubject(assignmentTitle),
    };
}
