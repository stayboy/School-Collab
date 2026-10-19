using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;

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
/// <para>Round notification-templates-b1: rendering goes through the
/// <see cref="INotificationTemplateResolver"/> (cross-context, fail-open to the built-in
/// defaults) and the pure <see cref="NotificationTemplateRenderer"/>; the resolved
/// template id is persisted on the row as the send-time record of what rendered. The
/// resolver is called once per channel per broadcast, never per recipient.</para>
///
/// <para>Recipients without an unexpired deep-link token — or with no resolvable
/// address — are recorded <see cref="NotificationDeliveryStatus.Skipped"/> with a
/// reason and are never sent (decision (c)).</para>
/// </summary>
public sealed class AssignmentNotificationBroadcaster(
    AssignmentsDbContext db,
    IContactAddressResolver addressResolver,
    IIntegrationEventPublisher publisher,
    INotificationTemplateResolver templateResolver,
    ITenantProvider tenantProvider,
    IOptions<NotificationLinkOptions> linkOptions,
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
        // One Settings read per channel per broadcast — never per recipient (§3.3's
        // resolution posture; the resolver's fail-open contract yields null → built-in
        // defaults). SchoolName comes from the tenant context, in-process (Q-C / §3.4).
        var templateCache = new Dictionary<NotificationChannel, ResolvedNotificationTemplate?>();
        var schoolName = tenantProvider.GetTenantContext().TenantName;
        foreach (var recipient in context.Recipients)
        {
            // E3 (ar-19): republish upsert-in-place. If a Publish row already exists
            // for this (tenant, assignment, recipient) key with the freshest rendered
            // payload, re-queue it (reset Attempt + re-stamp payload + due now) rather
            // than inserting a second Publish row — the partial unique index below is
            // the data-layer backstop, idempotency belongs here at the source.
            var existing = await db.NotificationLogs
                .FirstOrDefaultAsync(x => x.TenantId == recipient.TenantId
                    && x.AssignmentId == context.AssignmentId
                    && x.RecipientId == recipient.Id
                    && x.Kind == NotificationKind.Publish,
                    cancellationToken);

            var log = await QueueForRecipientAsync(existing, context, recipient, templateCache, schoolName, now, cancellationToken);
            // An existing Publish row is already tracked (upserted in place above); only
            // brand-new rows are added — re-`Add`-ing a tracked row would force a second insert.
            if (db.Entry(log).State == EntityState.Detached)
            {
                db.NotificationLogs.Add(log);
            }
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
        NotificationLog? existing,
        AssignmentPublishedContext context,
        AssignmentRecipient recipient,
        Dictionary<NotificationChannel, ResolvedNotificationTemplate?> templateCache,
        string schoolName,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var token = recipient.DeepLinkToken;
        var hasUnexpiredToken = !string.IsNullOrWhiteSpace(token)
            && recipient.DeepLinkExpiresAt is not null
            && recipient.DeepLinkExpiresAt > now;

        if (!hasUnexpiredToken)
        {
            return Skip(existing, context, recipient, string.Empty, string.Empty,
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
            return Skip(existing, context, recipient, string.Empty,
                NotificationMessageBuilder.BuildPublishSubject(context.Title),
                "no delivery address resolved", now);
        }

        // Resolve the tenant template for this channel once per broadcast (cache), then
        // render — fail-open to the built-in defaults when the Settings read yields null.
        var channel = (NotificationChannel)(int)recipient.Channel;
        if (!templateCache.TryGetValue(channel, out var template))
        {
            template = await templateResolver.ResolveAsync(
                NotificationKind.Publish, channel, recipient.TenantId, cancellationToken);
            templateCache[channel] = template;
        }

        var tagValues = new NotificationTagValues
        {
            AssignmentTitle = context.Title,
            Description = context.Description,
            DueDate = context.DueDate,
            Instructions = context.Instructions,
            // §3.4 enrichment: names arrive on the recipient from the publish-time Students
            // read; anything unresolved is string.Empty, so a missing name never fails the send.
            ContactName = recipient.ContactDisplayName,
            // §3.2 {{GuardianRole}}: the role rides the recipient row (Primary/CC); a
            // student-owned recipient has no guardian link, so the role is null → empty.
            GuardianRole = recipient.Role?.ToString() ?? string.Empty,
            StudentName = recipient.StudentDisplayName,
            SchoolName = schoolName,
            Link = BuildLink(recipient.DeepLinkToken!),
            LinkExpiry = recipient.DeepLinkExpiresAt,
        };
        var rendered = NotificationTemplateRenderer.Render(NotificationKind.Publish, channel, template, tagValues);
        return QueueOrUpdate(existing, context, recipient, address, rendered, now);
    }

    /// <summary>The recipient's link target: relative <c>/deeplink/{token}</c> by default;
    /// absolute when the Families public base URL is configured (S1's AppHost parameter —
    /// email clients cannot resolve relative links, so the deployment posture decides).</summary>
    private string BuildLink(string token) => linkOptions.Value.BuildDeepLink(token);

    private static NotificationLog QueueOrUpdate(
        NotificationLog? existing,
        AssignmentPublishedContext context,
        AssignmentRecipient recipient,
        string toAddress,
        RenderedMessage rendered,
        DateTimeOffset now)
    {
        // E3 (ar-19): existing Publish row → re-queue in place (freshest deep-link
        // payload wins); none → insert the standard Queued row. Single path — only
        // re-queue an already-existing row, otherwise the fresh row stands.
        var log = ExistingOrNew(existing, context, recipient, toAddress, rendered.Subject, rendered.Body, rendered.TemplateId, now);
        if (existing is not null)
        {
            log.Requeue(toAddress, rendered.Subject, rendered.Body, rendered.TemplateId, now);
        }
        return log;
    }

    private static NotificationLog Skip(
        NotificationLog? existing,
        AssignmentPublishedContext context,
        AssignmentRecipient recipient,
        string toAddress,
        string subject,
        string reason,
        DateTimeOffset now)
    {
        // Skipped rows never carry a rendered body — pass string.Empty so the persisted
        // row stays empty; `reason` is recorded on FailureReason, not the body. No
        // TemplateId: the skip is a policy outcome, not a rendered message.
        var log = ExistingOrNew(existing, context, recipient, toAddress, subject, string.Empty, templateId: null, now);
        // Existing or new, a skip re-stamps the same outcome (re-broadcast that is still
        // unroutable reuses the row, never a fresh insert).
        log.MarkSkipped(reason, now);
        return log;
    }

    private static NotificationLog ExistingOrNew(
        NotificationLog? existing,
        AssignmentPublishedContext context,
        AssignmentRecipient recipient,
        string toAddress,
        string subject,
        string bodyHtml,
        Guid? templateId,
        DateTimeOffset now)
    {
        if (existing is not null)
        {
            return existing;
        }

        var log = NotificationLog.Queue(
            recipient.TenantId,
            context.AssignmentId,
            recipient.Id,
            recipient.ContactId,
            (NotificationChannel)(int)recipient.Channel,
            NotificationKind.Publish,
            toAddress,
            subject,
            bodyHtml,
            templateId,
            now);
        return log;
    }
}
