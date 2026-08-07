using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Settings.Core.DTOs;

/// <summary>
/// Wire shape for a <see cref="Domain.TenantNotificationPolicy"/> (the global
/// default). Nullable fields so callers can distinguish "explicitly null" from a
/// value; the UI treats null as "no tenant default set, built-in default applies".
/// </summary>
public sealed record TenantNotificationPolicyDto(
    Guid Id,
    NotificationChannel[] PreferredChannelOrder,
    NotificationChannel[] BlockedChannels,
    int? MaxNotifications,
    int? MaxReminders,
    int? ReminderIntervalHours,
    int? LinkValidityDays,
    TimeOnly? SendoutTimeOfDay,
    int? SendoutIntervalMinutes,
    DateTimeOffset UpdatedAt);
