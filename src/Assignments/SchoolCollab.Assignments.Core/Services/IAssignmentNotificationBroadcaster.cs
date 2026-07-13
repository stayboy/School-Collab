using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Routes assignment notifications (published/due/submitted/graded/closed) to
/// the resolved <see cref="AssignmentRecipient"/> set, deduplicated by contact
/// (spec §4.6 / §Notifications). v1 routes the published event via the existing
/// outbox (<see cref="SchoolCollab.Core.Messaging.IIntegrationEventPublisher"/>);
/// actual email/SMS/WhatsApp delivery + per-contact consolidated content
/// (listing wards) is v1.1 (§18).
/// </summary>
public interface IAssignmentNotificationBroadcaster
{
    Task BroadcastPublishedAsync(AssignmentPublishedContext context, CancellationToken cancellationToken = default);
}

/// <summary>Context for a publish broadcast (spec §4.6). The recipient set is
/// already deduplicated by contact (one <see cref="AssignmentRecipient"/> per
/// subscribed contact); v1.1 will expand this into per-contact consolidated
/// notifications listing the affected wards.</summary>
public sealed record AssignmentPublishedContext(
    Guid AssignmentId,
    string Title,
    DateTimeOffset PublishedAt,
    IReadOnlyList<AssignmentRecipient> Recipients);