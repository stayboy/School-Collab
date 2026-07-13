using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Default <see cref="IAssignmentNotificationBroadcaster"/>. v1 enqueues a single
/// <see cref="AssignmentPublishedIntegrationEvent"/> via the shared outbox; the
/// recipient set is already persisted (deduped by contact). v1.1 will emit one
/// consolidated per-contact notification (listing wards) to the delivery channels
/// (§18) — that enrichment plugs in behind this interface without touching the
/// publish handler.
/// </summary>
public sealed class AssignmentNotificationBroadcaster(
    IIntegrationEventPublisher publisher,
    ILogger<AssignmentNotificationBroadcaster> logger) : IAssignmentNotificationBroadcaster
{
    public async Task BroadcastPublishedAsync(AssignmentPublishedContext context, CancellationToken cancellationToken = default)
    {
        await publisher.EnqueueAsync(
            new AssignmentPublishedIntegrationEvent(context.AssignmentId, context.Title, context.PublishedAt),
            cancellationToken);

        logger.LogInformation("Broadcast published notification for assignment {AssignmentId} to {Count} recipient(s)",
            context.AssignmentId, context.Recipients.Count);
    }
}