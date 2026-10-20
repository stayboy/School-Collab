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
/// notifications listing the affected wards.
/// <para>Round notification-templates-b1 (Q-E): the assignment-content fields the
/// renderer's tag values need ride the existing in-process record — the publish handler
/// already holds the entity, so widening adds zero extra reads. The outbox integration
/// event keeps its own (smaller) shape.</para>
/// <para>Round notification-templates-b2c (spec §3.7): the broadcaster has no DbContext, so the
/// template-selection inputs ride the record too — the assignment's live stamps (the author's
/// explicit choice, reused on republish) and its <see cref="AssignmentTemplateContext">evaluation
/// dimensions</see> (target kinds, topic, assignment type), both populated by the publish handler
/// from the entity it already holds.</para></summary>
public sealed record AssignmentPublishedContext(
    Guid AssignmentId,
    string Title,
    DateTimeOffset PublishedAt,
    string? Description,
    DateTimeOffset? DueDate,
    string? Instructions,
    IReadOnlyList<AssignmentRecipient> Recipients,
    AssignmentTemplateContext Dimensions,
    Guid? PublishEmailTemplateId = null,
    Guid? PublishSmsTemplateId = null);