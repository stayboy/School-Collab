using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// A guardian/student's subscription to notifications for a scope (spec §4.5).
/// v1 supports a single <see cref="SubscriptionScope.AllAssignments"/> scope.
/// New subscriptions default to <see cref="SubscriptionStatus.Unsubscribed"/>
/// (opted-out) until an explicit subscribe after verification (spec §2).
/// </summary>
public sealed class ContactSubscription : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private ContactSubscription() { }

    public Guid Id { get; private set; }
    public Guid ContactId { get; private set; }
    public SubscriptionScope Scope { get; private set; }
    public Guid? ScopeRefId { get; private set; }
    public SubscriptionStatus Status { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ContactSubscription Create(Guid contactId, SubscriptionScope scope, Guid? scopeRefId)
    {
        var now = DateTimeOffset.UtcNow;
        return new ContactSubscription
        {
            Id = Guid.NewGuid(),
            ContactId = contactId,
            Scope = scope,
            ScopeRefId = scopeRefId,
            Status = SubscriptionStatus.Unsubscribed,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Subscribe() { Status = SubscriptionStatus.Subscribed; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Unsubscribe() { Status = SubscriptionStatus.Unsubscribed; UpdatedAt = DateTimeOffset.UtcNow; }
}
