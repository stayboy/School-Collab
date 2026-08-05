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

    public Task<EffectiveNotificationPolicy> ResolveEffectiveAsync(
        Guid tenantId, Guid? gradeLevelId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Empty);
}
