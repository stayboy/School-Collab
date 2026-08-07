using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Applies the effective notification policy to a resolved recipient set at publish
/// time (notification-delivery-plan.md §3): drops recipients whose channel is blocked,
/// orders by preferred-channel order, and caps the sendout at
/// <c>MaxNotifications</c> (0 = no recipients are broadcast). Pure and deterministic so
/// it is unit-testable. <see cref="ContactChannel"/> (Students) and
/// <see cref="NotificationChannel"/> (Core) enums align by int value (Email=0, SMS=1,
/// WhatsApp=2), so channels are compared numerically.
/// </summary>
public static class NotificationRecipientFilter
{
    public static IReadOnlyList<AssignmentRecipient> Apply(
        IReadOnlyList<AssignmentRecipient> recipients, EffectiveNotificationPolicy policy)
    {
        if (recipients.Count == 0)
            return recipients;

        var blocked = policy.BlockedChannels.Select(c => (int)c).ToHashSet();
        var preferredIndex = policy.PreferredChannelOrder.Length == 0
            ? new Dictionary<int, int>()
            : policy.PreferredChannelOrder
                .Select((c, i) => new KeyValuePair<int, int>((int)c, i))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

        var ordered = recipients
            .Where(r => !blocked.Contains((int)r.Channel))
            .OrderBy(r => preferredIndex.TryGetValue((int)r.Channel, out var idx) ? idx : int.MaxValue)
            .ThenBy(r => (int)r.Channel)
            .ToList();

        if (policy.MaxNotifications is >= 0)
            ordered = ordered.Take(policy.MaxNotifications.Value).ToList();

        return ordered;
    }
}
