using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Default <see cref="IAssignmentNotificationBroadcaster"/>. Keeps the v1 outbox enqueue
/// (<see cref="AssignmentPublishedIntegrationEvent"/> — E3's future consumer) and adds
/// v1.1 delivery: for each <b>policy-filtered</b> recipient handed to it (the publish
/// handler applies <see cref="NotificationRecipientFilter"/> before this call) it
/// resolves the destination address, renders the consolidated per-contact message
/// carrying that recipient's <c>/deeplink/{token}</c>, and queues one
/// <see cref="NotificationLog"/> row (decision (e): one message per contact, not per
/// ward and not per channel duplicate).
///
/// <para>Recipients without an unexpired deep-link token — or with no resolvable
/// address — are recorded <see cref="NotificationDeliveryStatus.Skipped"/> with a
/// reason and are never sent (decision (c)).</para>
/// </summary>
public sealed class AssignmentNotificationBroadcaster(
    AssignmentsDbContext db,
    IContactAddressResolver addressResolver,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider,
    ILogger<AssignmentNotificationBroadcaster> logger) : IAssignmentNotificationBroadcaster
{
    /// <inheritdoc />
    public async Task BroadcastPublishedAsync(AssignmentPublishedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await publisher.EnqueueAsync(
            new AssignmentPublishedIntegrationEvent(context.AssignmentId, context.Title, context.PublishedAt),
            cancellationToken);

        var now = timeProvider.GetUtcNow();
        foreach (var recipient in context.Recipients)
        {
            db.NotificationLogs.Add(await QueueForRecipientAsync(context, recipient, now, cancellationToken));
        }

        if (context.Recipients.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Broadcast published notification for assignment {AssignmentId} to {Count} recipient(s)",
            context.AssignmentId, context.Recipients.Count);
    }

    private async Task<NotificationLog> QueueForRecipientAsync(
        AssignmentPublishedContext context,
        AssignmentRecipient recipient,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var token = recipient.DeepLinkToken;
        var hasUnexpiredToken = !string.IsNullOrWhiteSpace(token)
            && recipient.DeepLinkExpiresAt is not null
            && recipient.DeepLinkExpiresAt > now;

        if (!hasUnexpiredToken)
        {
            return Skipped(context, recipient, string.Empty, string.Empty,
                "recipient has no unexpired deep-link token", now);
        }

        string? address;
        try
        {
            address = await addressResolver.ResolveAddressAsync(
                recipient.ContactId, recipient.OwnerType, recipient.OwnerId, cancellationToken);
        }
        // Genuine cancellation (client disconnect / shutdown) must NOT be converted into a
        // terminal Skipped row — the notification would be lost. Let it propagate so the
        // publish aborts and the row is never queued. A transport timeout (token not
        // cancelled) still falls through to Skipped below, which is non-aborting.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve the delivery address for contact {ContactId}", recipient.ContactId);
            address = null;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            return Skipped(context, recipient, string.Empty,
                NotificationMessageBuilder.BuildPublishSubject(context.Title),
                "no delivery address resolved", now);
        }

        if (recipient.Channel == Students.Core.Domain.ContactChannel.Email)
        {
            var message = NotificationMessageBuilder.BuildPublishEmail(address, context.Title, token!);
            return Queue(context, recipient, message.To, message.Subject, message.BodyHtml, now);
        }

        // SMS / WhatsApp share one text body (no provider in v1 — LogAndSkipSmsSender).
        var sms = NotificationMessageBuilder.BuildPublishSms(address, context.Title, token!);
        return Queue(context, recipient, sms.To, NotificationMessageBuilder.BuildPublishSubject(context.Title), sms.Body, now);
    }

    private static NotificationLog Queue(
        AssignmentPublishedContext context,
        AssignmentRecipient recipient,
        string toAddress,
        string subject,
        string bodyHtml,
        DateTimeOffset now) =>
        NotificationLog.Queue(
            recipient.TenantId,
            context.AssignmentId,
            recipient.Id,
            recipient.ContactId,
            (NotificationChannel)(int)recipient.Channel,
            NotificationKind.Publish,
            toAddress,
            subject,
            bodyHtml,
            now);

    private static NotificationLog Skipped(
        AssignmentPublishedContext context,
        AssignmentRecipient recipient,
        string toAddress,
        string subject,
        string reason,
        DateTimeOffset now)
    {
        var log = Queue(context, recipient, toAddress, subject, string.Empty, now);
        log.MarkSkipped(reason, now);
        return log;
    }
}
