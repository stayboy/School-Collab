using SchoolCollab.Core.Data;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Optional per-grade <b>override</b> notification policy (notification-delivery-plan.md §2).
/// At most one row per (tenant, grade). <b>Null field = inherit the tenant default</b>; a
/// non-null field overrides it. The <see cref="Core.Notifications.EffectiveNotificationPolicyResolver"/>
/// merges this with the tenant-global default and reports per-field source flags.
///
/// <para><b>Delivery is out of scope</b> for this phase; the reminder/sendout fields are
/// stored and surfaced for the eventual §18 delivery feature but not yet enforced.</para>
/// </summary>
public sealed class GradeNotificationPolicy : BaseTenantEntityWithAudit, IHasRowVersion
{
    private GradeNotificationPolicy() { }

    public Guid GradeLevelId { get; private set; }

    public NotificationChannel[]? PreferredChannelOrder { get; private set; }
    public NotificationChannel[]? BlockedChannels { get; private set; }

    public int? MaxNotifications { get; private set; }
    public int? MaxReminders { get; private set; }
    public int? ReminderIntervalHours { get; private set; }
    public int? LinkValidityDays { get; private set; }
    public TimeOnly? SendoutTimeOfDay { get; private set; }
    public int? SendoutIntervalMinutes { get; private set; }

    public uint RowVersion { get; private set; }

    public static GradeNotificationPolicy Create(
        Guid tenantId,
        Guid gradeLevelId,
        NotificationChannel[]? preferredChannelOrder = null,
        NotificationChannel[]? blockedChannels = null,
        int? maxNotifications = null,
        int? maxReminders = null,
        int? reminderIntervalHours = null,
        int? linkValidityDays = null,
        TimeOnly? sendoutTimeOfDay = null,
        int? sendoutIntervalMinutes = null)
    {
        ValidateCounts(maxNotifications, maxReminders, reminderIntervalHours,
            linkValidityDays, sendoutIntervalMinutes);

        var now = DateTimeOffset.UtcNow;
        return new GradeNotificationPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            GradeLevelId = gradeLevelId,
            PreferredChannelOrder = preferredChannelOrder,
            BlockedChannels = blockedChannels,
            MaxNotifications = maxNotifications,
            MaxReminders = maxReminders,
            ReminderIntervalHours = reminderIntervalHours,
            LinkValidityDays = linkValidityDays,
            SendoutTimeOfDay = sendoutTimeOfDay,
            SendoutIntervalMinutes = sendoutIntervalMinutes,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Replaces the override. Null fields become "inherit" (cleared to null). Stamps
    /// <see cref="UpdatedAt"/>.
    /// </summary>
    public void SetOverride(
        NotificationChannel[]? preferredChannelOrder,
        NotificationChannel[]? blockedChannels,
        int? maxNotifications,
        int? maxReminders,
        int? reminderIntervalHours,
        int? linkValidityDays,
        TimeOnly? sendoutTimeOfDay,
        int? sendoutIntervalMinutes)
    {
        ValidateCounts(maxNotifications, maxReminders, reminderIntervalHours,
            linkValidityDays, sendoutIntervalMinutes);

        PreferredChannelOrder = preferredChannelOrder;
        BlockedChannels = blockedChannels;
        MaxNotifications = maxNotifications;
        MaxReminders = maxReminders;
        ReminderIntervalHours = reminderIntervalHours;
        LinkValidityDays = linkValidityDays;
        SendoutTimeOfDay = sendoutTimeOfDay;
        SendoutIntervalMinutes = sendoutIntervalMinutes;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateCounts(
        int? maxNotifications, int? maxReminders, int? reminderIntervalHours,
        int? linkValidityDays, int? sendoutIntervalMinutes)
    {
        if (maxNotifications is < 0) throw new ArgumentOutOfRangeException(nameof(maxNotifications), "MaxNotifications cannot be negative.");
        if (maxReminders is < 0) throw new ArgumentOutOfRangeException(nameof(maxReminders), "MaxReminders cannot be negative.");
        if (reminderIntervalHours is < 1) throw new ArgumentOutOfRangeException(nameof(reminderIntervalHours), "ReminderIntervalHours must be >= 1.");
        if (linkValidityDays is < 1) throw new ArgumentOutOfRangeException(nameof(linkValidityDays), "LinkValidityDays must be >= 1.");
        if (sendoutIntervalMinutes is < 1) throw new ArgumentOutOfRangeException(nameof(sendoutIntervalMinutes), "SendoutIntervalMinutes must be >= 1.");
    }
}
