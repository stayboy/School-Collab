using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// Per-(assignment, contact) publish recipient (spec §4.6). One row per
/// <em>subscribed</em> contact reached by a publish. For guardian-owned
/// contacts the <see cref="Role"/> is mirrored from the
/// <c>StudentGuardian</c> link (Primary/CC); <see cref="WardStudentId"/> is
/// set for direct guardian publishes. The student is only a target if they
/// have a subscribed contact (no recipient rows ⇒ not notified).
/// </summary>
public sealed class AssignmentRecipient : ITenantEntity, IEntity, IAuditableEntity
{
    private AssignmentRecipient() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }

    public Guid AssignmentId { get; private set; }
    public ContactOwnerType OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid? WardStudentId { get; private set; }
    public Guid ContactId { get; private set; }
    public ContactChannel Channel { get; private set; }
    public GuardianRole? Role { get; private set; }
    public bool NotifyOnBroadcast { get; private set; }
    public bool SubscriptionActive { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? OpenedAt { get; private set; }

    // WS-E1 (ar-14-deep-links): the contact-scoped deep-link bearer token (protected
    // ciphertext) minted at publish, plus when it expires. One token per
    // (assignment, contact) recipient row — re-minted/reused on republish.
    public string? DeepLinkToken { get; private set; }
    public DateTimeOffset? DeepLinkExpiresAt { get; private set; }

    /// <summary>
    /// Round notification-templates-b1 (§3.4): the notified contact's display name, carried
    /// <b>in process only</b> from the publish-time Students read so the renderer's
    /// <c>{{GuardianName}}</c> tag carries a real value. Never persisted — the recipient row
    /// stores ids, and the name degrades to <see cref="string.Empty"/> when enrichment did not
    /// resolve it.
    /// </summary>
    [NotMapped]
    public string ContactDisplayName { get; private set; } = string.Empty;

    /// <summary>The ward student's display name for this recipient (the renderer's
    /// <c>{{StudentName}}</c>), in process only — same enrichment contract as
    /// <see cref="ContactDisplayName"/>.</summary>
    [NotMapped]
    public string StudentDisplayName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AssignmentRecipient Create(
        Guid tenantId,
        Guid assignmentId,
        ContactOwnerType ownerType,
        Guid ownerId,
        Guid? wardStudentId,
        Guid contactId,
        ContactChannel channel,
        GuardianRole? role,
        bool notifyOnBroadcast,
        bool subscriptionActive)
    {
        var now = DateTimeOffset.UtcNow;
        return new AssignmentRecipient
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AssignmentId = assignmentId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            WardStudentId = wardStudentId,
            ContactId = contactId,
            Channel = channel,
            Role = role,
            NotifyOnBroadcast = notifyOnBroadcast,
            SubscriptionActive = subscriptionActive,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void MarkDelivered()
    {
        DeliveredAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Idempotent republish: keep the recipient subscribed + flagged for broadcast.</summary>
    public void MarkSubscribed(bool notifyOnBroadcast = true)
    {
        SubscriptionActive = true;
        NotifyOnBroadcast = notifyOnBroadcast;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkOpened()
    {
        OpenedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>WS-E1 (ar-14-deep-links) — stamps the deep-link token (minted)
    /// and its expiry, or refreshes both on a republish re-mint. Additive; touches
    /// only the deep-link columns plus the audit timestamp.</summary>
    public void AttachDeepLink(string token, DateTimeOffset expiresAt)
    {
        DeepLinkToken = token;
        DeepLinkExpiresAt = expiresAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Round notification-templates-b1 (§3.4) — carries the enrichment names the
    /// renderer needs onto the in-process recipient. Transient (see
    /// <see cref="ContactDisplayName"/>): never persisted, never part of a save.</summary>
    public void AttachDisplayNames(string contactName, string studentName)
    {
        ContactDisplayName = contactName;
        StudentDisplayName = studentName;
    }
}
