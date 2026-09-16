using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>Returns an empty effective policy (no blocked channels, no preferred
/// order, no cap) so existing handler tests keep their pre-policy behavior.</summary>
internal sealed class FakeNotificationPolicyResolver : INotificationPolicyResolver
{
    public static readonly EffectiveNotificationPolicy Empty = new(
        PreferredChannelOrder: [], BlockedChannels: [], MaxNotifications: null,
        MaxReminders: null, ReminderIntervalHours: null, LinkValidityDays: null,
        SendoutTimeOfDay: null, SendoutIntervalMinutes: null,
        PreferredChannelOrderFromOverride: false, BlockedChannelsFromOverride: false,
        MaxNotificationsFromOverride: false, MaxRemindersFromOverride: false,
        ReminderIntervalHoursFromOverride: false, LinkValidityDaysFromOverride: false,
        SendoutTimeOfDayFromOverride: false, SendoutIntervalMinutesFromOverride: false);

    private readonly int? _linkValidityDays;

    /// <summary>Resolves the empty policy with an optional LinkValidityDays so
    /// deep-link expiry tests can control the resolved value.</summary>
    public FakeNotificationPolicyResolver(int? linkValidityDays = null)
        => _linkValidityDays = linkValidityDays;

    public Task<EffectiveNotificationPolicy> ResolveEffectiveAsync(
        Guid tenantId, Guid? gradeLevelId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_linkValidityDays is null
            ? Empty
            : Empty with { LinkValidityDays = _linkValidityDays });
}
