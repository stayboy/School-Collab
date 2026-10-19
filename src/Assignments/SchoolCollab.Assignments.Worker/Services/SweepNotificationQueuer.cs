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
/// Round notification-templates-b2a (WS3) — the pass-scoped render inputs every queued recipient of
/// one sweep pass shares: the tenant template resolver, the link composer and a per-pass template
/// cache, so a pass reads each (kind, channel) slot from Settings <b>once</b> rather than once per
/// recipient (the broadcaster's "never per recipient" posture, §3.3).
/// </summary>
public sealed record SweepRenderDependencies(
    INotificationTemplateResolver TemplateResolver,
    NotificationLinkOptions LinkOptions,
    IDictionary<(NotificationKind Kind, NotificationChannel Channel), ResolvedNotificationTemplate?> TemplateCache);

/// <summary>
/// Shared render + queue step for the E3 sweeps (reminder / completion / overdue).
/// Mirrors the broadcaster's queue-time posture (decision (b)/(c)): the destination
/// address is resolved once and rendered into the <see cref="NotificationLog"/> row at
/// queue time; a recipient with no unexpired deep-link token or no resolvable address is
/// recorded <see cref="NotificationDeliveryStatus.Skipped"/> and never sent.
///
/// <para>Round notification-templates-b2a (WS3): rendering goes through the SAME B1 path the
/// publish flow uses — <see cref="INotificationTemplateResolver"/> (fail-open) →
/// <see cref="NotificationTemplateRenderer"/> (built-in defaults when no template is set) — so an
/// edited Reminder/Completion/Overdue template renders here exactly as Publish does, and the
/// resolved template id is persisted on the row (null = built-in default). Links are composed
/// through <see cref="NotificationLinkOptions.BuildDeepLink"/>, so an SMS/WhatsApp sweep message
/// carries the short link. A plain-text recipient whose row predates short links is
/// <b>ensure-minted</b> here (§3.6 / Q64): the code is added to the same row in the same
/// transaction, idempotently.</para>
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
        SweepRenderDependencies render,
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

        // The sweeps own exactly the three E3 kinds; anything else is a sweep bug, not a message.
        if (kind is not (NotificationKind.Reminder or NotificationKind.Completion or NotificationKind.Overdue))
        {
            throw new NotificationKindNotSupportedException($"No message template for notification kind {kind}.");
        }

        var shortLinkCode = await EnsureShortLinkCodeAsync(db, channel, recipientId, cancellationToken);

        var template = await ResolveTemplateAsync(render, kind, channel, tenantId, cancellationToken);
        var tags = new NotificationTagValues
        {
            AssignmentTitle = assignmentTitle,
            // The sweep candidate read carries no description/instructions/names (§3.4 enrichment is
            // a publish-path concern), so those tags degrade to empty exactly as an unresolved
            // publish-name does; the link and its expiry ARE known here.
            Link = render.LinkOptions.BuildDeepLink(deepLinkToken!, channel, shortLinkCode),
            LinkExpiry = deepLinkExpiresAt,
        };
        var rendered = NotificationTemplateRenderer.Render(kind, channel, template, tags);

        var row = NotificationLog.Queue(tenantId, assignmentId, recipientId, contactId, channel, kind,
            address, rendered.Subject, rendered.Body, rendered.TemplateId, now);

        db.NotificationLogs.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// §3.6 / Q64 ensure-mint: an SMS/WhatsApp recipient whose row predates short links gets a code
    /// minted onto it, idempotently (an existing code is never replaced, and the same transaction
    /// persists it with the queued row). Email recipients keep the full deep link and load no row.
    /// Returns the code the message's link is composed from.
    /// </summary>
    private static async Task<string?> EnsureShortLinkCodeAsync(
        AssignmentsDbContext db, NotificationChannel channel, Guid recipientId, CancellationToken ct)
    {
        if (channel == NotificationChannel.Email)
        {
            return null;
        }

        var recipient = await db.AssignmentRecipients
            .FirstOrDefaultAsync(x => x.Id == recipientId, ct);

        // The candidate read came from this same table inside the tenant context established by the
        // sweeper; a missing row means the recipient vanished between read and dispatch — the link
        // then degrades to the full deep link rather than failing the queue.
        if (recipient is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(recipient.ShortLinkCode))
        {
            recipient.EnsureShortLinkCode(await ShortLinkCodes.MintUniqueAsync(
                (candidate, token) => db.AssignmentRecipients
                    .IgnoreQueryFilters(["Tenant"])
                    .AnyAsync(x => x.ShortLinkCode == candidate, token),
                ct));
        }

        return recipient.ShortLinkCode;
    }

    /// <summary>One Settings read per (kind, channel) per sweep pass — the broadcaster's cache
    /// posture, held in <see cref="SweepRenderDependencies.TemplateCache"/>.</summary>
    private static async Task<ResolvedNotificationTemplate?> ResolveTemplateAsync(
        SweepRenderDependencies render,
        NotificationKind kind,
        NotificationChannel channel,
        Guid tenantId,
        CancellationToken ct)
    {
        var key = (kind, channel);
        if (render.TemplateCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var resolved = await render.TemplateResolver.ResolveAsync(kind, channel, tenantId, ct);
        render.TemplateCache[key] = resolved;
        return resolved;
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
            string.Empty, subject, string.Empty, null, now);
        row.MarkSkipped(reason, now);
        db.NotificationLogs.Add(row);
        await db.SaveChangesAsync(cancellationToken);
    }
}
