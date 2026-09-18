using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Worker.Services;

/// <summary>
/// Shared render + queue step for the E3 sweeps (reminder / completion / overdue).
/// Mirrors the broadcaster's queue-time posture (decision (b)/(c)): the destination
/// address is resolved once and rendered into the <see cref="NotificationLog"/> row at
/// queue time; a recipient with no unexpired deep-link token or no resolvable address is
/// recorded <see cref="NotificationDeliveryStatus.Skipped"/> and never sent.
/// </summary>
public static class SweepNotificationQueuer
{
    /// <summary>
    /// Renders + queues one <see cref="NotificationKind.Reminder"/> / Completion /
    /// Overdue <see cref="NotificationLog"/> row <paramref name="kind"/> for the given
    /// recipient. Returns <see langword="true"/> when a <c>Queued</c> row was produced;
    /// a <c>Skipped</c> row is still persisted but returns <see langword="false"/>.
    /// </summary>
    public static async Task<bool> QueueAsync(
        AssignmentsDbContext db,
        IContactAddressResolver addressResolver,
        ILogger logger,
        TimeProvider timeProvider,
        Guid tenantId,
        Guid assignmentId,
        string assignmentTitle,
        Guid recipientId,
        Guid contactId,
        ContactOwnerType ownerType,
        Guid ownerId,
        NotificationChannel channel,
        string? deepLinkToken,
        DateTimeOffset? deepLinkExpiresAt,
        NotificationKind kind,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var hasUnexpiredToken = !string.IsNullOrWhiteSpace(deepLinkToken)
            && deepLinkExpiresAt is not null
            && deepLinkExpiresAt > now;

        if (!hasUnexpiredToken)
        {
            await PersistSkipAsync(db, tenantId, assignmentId, recipientId, contactId, channel, kind,
                NotificationMessageBuilder.SubjectFor(kind, assignmentTitle),
                "recipient has no unexpired deep-link token", now, cancellationToken);
            return false;
        }

        string? address;
        try
        {
            address = await addressResolver.ResolveAddressAsync(contactId, ownerType, ownerId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve the delivery address for contact {ContactId}", contactId);
            address = null;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            await PersistSkipAsync(db, tenantId, assignmentId, recipientId, contactId, channel, kind,
                NotificationMessageBuilder.SubjectFor(kind, assignmentTitle),
                "no delivery address resolved", now, cancellationToken);
            return false;
        }

        var isEmail = channel == NotificationChannel.Email;
        NotificationLog row;
        switch (kind)
        {
            case NotificationKind.Reminder:
                if (isEmail)
                {
                    var m = NotificationMessageBuilder.BuildReminderEmail(address, assignmentTitle, deepLinkToken!);
                    row = NotificationLog.Queue(tenantId, assignmentId, recipientId, contactId, channel, kind,
                        m.To, m.Subject, m.BodyHtml, now);
                }
                else
                {
                    var m = NotificationMessageBuilder.BuildReminderSms(address, assignmentTitle, deepLinkToken!);
                    row = NotificationLog.Queue(tenantId, assignmentId, recipientId, contactId, channel, kind,
                        m.To, NotificationMessageBuilder.BuildReminderSubject(assignmentTitle), m.Body, now);
                }
                break;
            case NotificationKind.Completion:
                if (isEmail)
                {
                    var m = NotificationMessageBuilder.BuildCompletionEmail(address, assignmentTitle, deepLinkToken!);
                    row = NotificationLog.Queue(tenantId, assignmentId, recipientId, contactId, channel, kind,
                        m.To, m.Subject, m.BodyHtml, now);
                }
                else
                {
                    var m = NotificationMessageBuilder.BuildCompletionSms(address, assignmentTitle, deepLinkToken!);
                    row = NotificationLog.Queue(tenantId, assignmentId, recipientId, contactId, channel, kind,
                        m.To, NotificationMessageBuilder.BuildCompletionSubject(assignmentTitle), m.Body, now);
                }
                break;
            case NotificationKind.Overdue:
                if (isEmail)
                {
                    var m = NotificationMessageBuilder.BuildOverdueEmail(address, assignmentTitle, deepLinkToken!);
                    row = NotificationLog.Queue(tenantId, assignmentId, recipientId, contactId, channel, kind,
                        m.To, m.Subject, m.BodyHtml, now);
                }
                else
                {
                    var m = NotificationMessageBuilder.BuildOverdueSms(address, assignmentTitle, deepLinkToken!);
                    row = NotificationLog.Queue(tenantId, assignmentId, recipientId, contactId, channel, kind,
                        m.To, NotificationMessageBuilder.BuildOverdueSubject(assignmentTitle), m.Body, now);
                }
                break;
            default:
                throw new NotificationKindNotSupportedException($"No message template for notification kind {kind}.");
        }

        db.NotificationLogs.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Persists a terminal <c>Skipped</c> row for the given
    /// (tenant, assignment, recipient, kind) key. Idempotent (P1-2): if a <c>Skipped</c>
    /// row already exists for the key it is reused — never a second insert — so an
    /// unresolvable recipient does not accumulate a Skipped row every sweep pass.</summary>
    private static async Task PersistSkipAsync(
        AssignmentsDbContext db,
        Guid tenantId,
        Guid assignmentId,
        Guid recipientId,
        Guid contactId,
        NotificationChannel channel,
        NotificationKind kind,
        string subject,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await db.NotificationLogs.FirstOrDefaultAsync(
            x => x.TenantId == tenantId
                && x.AssignmentId == assignmentId
                && x.RecipientId == recipientId
                && x.Kind == kind
                && x.DeliveryStatus == NotificationDeliveryStatus.Skipped,
            cancellationToken);
        if (existing is not null)
        {
            return; // already skipped for this key — terminal coverage.
        }

        var row = NotificationLog.Queue(tenantId, assignmentId, recipientId, contactId, channel, kind,
            string.Empty, subject, string.Empty, now);
        row.MarkSkipped(reason, now);
        db.NotificationLogs.Add(row);
        await db.SaveChangesAsync(cancellationToken);
    }
}
