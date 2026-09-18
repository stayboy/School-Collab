using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// WS-E2 (ar-16) — one row per notification handed to a delivery channel for a
/// policy-filtered recipient (spec §4 <c>NotificationLog</c>). Standalone tenant
/// entity: the payload is <b>rendered and persisted at queue time</b> (decision (b)),
/// so a retry re-sends exactly the same <see cref="ToAddress"/> /
/// <see cref="Subject"/> / <see cref="BodyHtml"/> with no cross-context lookup and no
/// chance of the message changing under the recipient. State transitions are
/// intention-revealing methods — there are no public setters.
/// </summary>
public sealed class NotificationLog : BaseTenantEntityWithAudit
{
    private NotificationLog() { }

    private NotificationLog(Guid tenantId) : base(tenantId) { }

    /// <summary>The recipient row (<c>AssignmentRecipient</c>) this notification is for.</summary>
    public Guid RecipientId { get; private set; }

    /// <summary>The assignment the notification concerns.</summary>
    public Guid AssignmentId { get; private set; }

    /// <summary>The notified contact (Students bounded context id).</summary>
    public Guid ContactId { get; private set; }

    /// <summary>The channel this row is delivered on.</summary>
    public NotificationChannel Channel { get; private set; }

    /// <summary>Why the notification was raised.</summary>
    public NotificationKind Kind { get; private set; }

    /// <summary>Delivery attempts made so far (successful or failed).</summary>
    public int Attempt { get; private set; }

    /// <summary>Current lifecycle status.</summary>
    public NotificationDeliveryStatus DeliveryStatus { get; private set; }

    /// <summary>When the message was handed to the channel successfully.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Why the last attempt failed, or why the row was skipped.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>When the next retry is due; null = no retry scheduled (terminal).</summary>
    public DateTimeOffset? NextRetryAt { get; private set; }

    /// <summary>Rendered destination address (email address / phone number) at queue time.</summary>
    public string ToAddress { get; private set; } = string.Empty;

    /// <summary>Rendered subject at queue time.</summary>
    public string Subject { get; private set; } = string.Empty;

    /// <summary>Rendered body at queue time (HTML for email, plain text for SMS).</summary>
    public string BodyHtml { get; private set; } = string.Empty;

    /// <summary>
    /// Queues a notification with its already-rendered payload. The row starts
    /// <see cref="NotificationDeliveryStatus.Queued"/> with <c>Attempt = 0</c> and is
    /// immediately due (<paramref name="now"/>).
    /// </summary>
    public static NotificationLog Queue(
        Guid tenantId,
        Guid assignmentId,
        Guid recipientId,
        Guid contactId,
        NotificationChannel channel,
        NotificationKind kind,
        string toAddress,
        string subject,
        string bodyHtml,
        DateTimeOffset now)
    {
        var log = new NotificationLog(tenantId)
        {
            AssignmentId = assignmentId,
            RecipientId = recipientId,
            ContactId = contactId,
            Channel = channel,
            Kind = kind,
            Attempt = 0,
            DeliveryStatus = NotificationDeliveryStatus.Queued,
            NextRetryAt = now,
            ToAddress = toAddress,
            Subject = subject,
            BodyHtml = bodyHtml,
        };
        return log;
    }

    /// <summary>Records a successful delivery attempt. Terminal.</summary>
    public void MarkSent(DateTimeOffset now)
    {
        Attempt++;
        DeliveryStatus = NotificationDeliveryStatus.Sent;
        SentAt = now;
        FailureReason = null;
        NextRetryAt = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records a failed delivery attempt. <paramref name="nextRetryAt"/> null makes the
    /// failure terminal (the attempt cap was reached).
    /// </summary>
    public void MarkFailed(string reason, DateTimeOffset? nextRetryAt, DateTimeOffset now)
    {
        Attempt++;
        DeliveryStatus = NotificationDeliveryStatus.Failed;
        FailureReason = reason;
        NextRetryAt = nextRetryAt;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records that the notification is deliberately not sent (no deep-link token, an
    /// expired token, or no resolvable address). Terminal, and never a failure —
    /// skips are expected policy outcomes (decision (c)).
    /// </summary>
    public void MarkSkipped(string reason, DateTimeOffset now)
    {
        DeliveryStatus = NotificationDeliveryStatus.Skipped;
        FailureReason = reason;
        NextRetryAt = null;
        UpdatedAt = now;
    }

    /// <summary>E3 (ar-19) — re-queues an existing row with a freshly rendered payload
    /// (republish re-broadcast / reminder re-send). Resets the attempt cycle
    /// (<c>Attempt = 0</c>), re-stamps the payload, and makes the row due now. Used by
    /// the broadcaster's republish <b>upsert-in-place</b> path so a re-broadcast
    /// refreshes the deep-link-token message without violating the Publish uniqueness
    /// index. Transitions a previously <c>Skipped</c> row back to <c>Queued</c> — the
    /// republish decision is a fresh queued decision, so a skip whose address now
    /// resolves is deliberately revived.</summary>
    public void Requeue(string toAddress, string subject, string bodyHtml, DateTimeOffset now)
    {
        Attempt = 0;
        DeliveryStatus = NotificationDeliveryStatus.Queued;
        SentAt = null;
        FailureReason = null;
        NextRetryAt = now;
        ToAddress = toAddress;
        Subject = subject;
        BodyHtml = bodyHtml;
        UpdatedAt = now;
    }
}
