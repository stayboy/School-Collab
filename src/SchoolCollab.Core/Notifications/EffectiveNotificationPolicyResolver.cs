namespace SchoolCollab.Core.Notifications;

/// <summary>
/// Default merge implementation: a non-null grade override field wins; otherwise the
/// tenant default value is used. The <c>*FromOverride</c> flag reports per field whether
/// the resolved value came from the grade override (true) or was inherited / unset
/// (false). Channel lists follow the same null-means-inherit rule, so an explicitly empty
/// grade list overrides a non-empty tenant list.
/// </summary>
public sealed class EffectiveNotificationPolicyResolver : IEffectiveNotificationPolicyResolver
{
    public EffectiveNotificationPolicy Resolve(
        NotificationPolicyFields? tenantDefault,
        NotificationPolicyFields? gradeOverride)
    {
        var tenant = tenantDefault ?? NotificationPolicyFields.Empty;

        return new EffectiveNotificationPolicy(
            PreferredChannelOrder: gradeOverride?.PreferredChannelOrder ?? tenant.PreferredChannelOrder ?? [],
            BlockedChannels: gradeOverride?.BlockedChannels ?? tenant.BlockedChannels ?? [],
            MaxNotifications: gradeOverride?.MaxNotifications ?? tenant.MaxNotifications,
            MaxReminders: gradeOverride?.MaxReminders ?? tenant.MaxReminders,
            ReminderIntervalHours: gradeOverride?.ReminderIntervalHours ?? tenant.ReminderIntervalHours,
            LinkValidityDays: gradeOverride?.LinkValidityDays ?? tenant.LinkValidityDays,
            SendoutTimeOfDay: gradeOverride?.SendoutTimeOfDay ?? tenant.SendoutTimeOfDay,
            SendoutIntervalMinutes: gradeOverride?.SendoutIntervalMinutes ?? tenant.SendoutIntervalMinutes,
            PreferredChannelOrderFromOverride: gradeOverride?.PreferredChannelOrder is not null,
            BlockedChannelsFromOverride: gradeOverride?.BlockedChannels is not null,
            MaxNotificationsFromOverride: gradeOverride?.MaxNotifications is not null,
            MaxRemindersFromOverride: gradeOverride?.MaxReminders is not null,
            ReminderIntervalHoursFromOverride: gradeOverride?.ReminderIntervalHours is not null,
            LinkValidityDaysFromOverride: gradeOverride?.LinkValidityDays is not null,
            SendoutTimeOfDayFromOverride: gradeOverride?.SendoutTimeOfDay is not null,
            SendoutIntervalMinutesFromOverride: gradeOverride?.SendoutIntervalMinutes is not null);
    }
}
