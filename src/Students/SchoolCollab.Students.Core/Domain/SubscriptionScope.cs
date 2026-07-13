namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Subscription scope for a <see cref="ContactSubscription"/> (spec §4.5).
/// v1 supports a single global scope; scoped subscriptions are a later feature.
/// </summary>
public enum SubscriptionScope
{
    AllAssignments = 0
}
