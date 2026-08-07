using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Settings.Core.CQRS.NotificationPolicies.Commands.UpsertTenantNotificationPolicy;

/// <summary>
/// Creates or replaces the current tenant's global default notification policy.
/// The returned <see cref="TenantNotificationPolicyDto"/> is the persisted policy
/// (count fields normalised — a negative/zero value is rejected by the domain).
/// </summary>
public sealed record UpsertTenantNotificationPolicy(
    NotificationChannel[]? PreferredChannelOrder,
    NotificationChannel[]? BlockedChannels,
    int? MaxNotifications,
    int? MaxReminders,
    int? ReminderIntervalHours,
    int? LinkValidityDays,
    TimeOnly? SendoutTimeOfDay,
    int? SendoutIntervalMinutes) : ICommand;
