namespace SchoolCollab.Core.Notifications;

/// <summary>
/// The <b>resolved</b> (merged) policy for a grade: each field's effective value plus a
/// per-field flag reporting whether that value came from the grade override (true) or
/// was inherited from the tenant default / left unset (false). Computed by
/// <see cref="IEffectiveNotificationPolicyResolver"/> (notification-delivery-plan.md §2).
/// </summary>
public sealed record EffectiveNotificationPolicy(
    NotificationChannel[] PreferredChannelOrder,
    NotificationChannel[] BlockedChannels,
    int? MaxNotifications,
    int? MaxReminders,
    int? ReminderIntervalHours,
    int? LinkValidityDays,
    TimeOnly? SendoutTimeOfDay,
    int? SendoutIntervalMinutes,
    bool PreferredChannelOrderFromOverride,
    bool BlockedChannelsFromOverride,
    bool MaxNotificationsFromOverride,
    bool MaxRemindersFromOverride,
    bool ReminderIntervalHoursFromOverride,
    bool LinkValidityDaysFromOverride,
    bool SendoutTimeOfDayFromOverride,
    bool SendoutIntervalMinutesFromOverride);
