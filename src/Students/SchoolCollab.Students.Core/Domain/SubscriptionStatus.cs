namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Subscription status for a <see cref="ContactSubscription"/> (spec §4.5).
/// New contacts default to <see cref="Unsubscribed"/> (opted-out) until an
/// explicit subscribe after verification (spec §2).
/// </summary>
public enum SubscriptionStatus
{
    Unsubscribed = 0,
    Subscribed = 1
}
