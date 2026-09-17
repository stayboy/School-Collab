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
}
