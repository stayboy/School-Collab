using SchoolCollab.Core.Data;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// Per-tenant <b>global default</b> notification policy (grade-level-detail-view-plan.md §9 /
/// notification-delivery-plan.md §2). One row per tenant. A per-grade
/// <see cref="GradeNotificationPolicy"/> may override individual fields; any field left
/// null here is simply unset (the system applies built-in defaults at publish time).
///
/// <para><b>Delivery is out of scope</b> for this phase: the reminder/sendout fields
/// (<see cref="MaxReminders"/>, <see cref="ReminderIntervalHours"/>,
/// <see cref="LinkValidityDays"/>, <see cref="SendoutTimeOfDay"/>,
/// <see cref="SendoutIntervalMinutes"/>) are stored and surfaced for the eventual §18
/// delivery feature but are not yet enforced by any worker.</para>
/// </summary>
public sealed class TenantNotificationPolicy : BaseTenantEntityWithAudit, IHasRowVersion
{
    private TenantNotificationPolicy() { }

    public NotificationChannel[] PreferredChannelOrder { get; private set; } = [];
    public NotificationChannel[] BlockedChannels { get; private set; } = [];

    public int? MaxNotifications { get; private set; }
    public int? MaxReminders { get; private set; }
    public int? ReminderIntervalHours { get; private set; }
    public int? LinkValidityDays { get; private set; }
    public TimeOnly? SendoutTimeOfDay { get; private set; }
    public int? SendoutIntervalMinutes { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>
    /// Creates (or, on re-invocation, updates) the single policy row for
    /// <paramref name="tenantId"/>. Callers typically use
    /// <see cref="SetPolicy"/> on an existing row instead; this factory is for the
    /// initial insert.
    /// </summary>
    public static TenantNotificationPolicy Create(
        Guid tenantId,
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
        return new TenantNotificationPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PreferredChannelOrder = preferredChannelOrder ?? [],
            BlockedChannels = blockedChannels ?? [],
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
    /// Replaces the whole policy with the supplied values (null count fields are
    /// treated as "cleared"). Stamps <see cref="UpdatedAt"/>.
    /// </summary>
    public void SetPolicy(
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

        PreferredChannelOrder = preferredChannelOrder ?? [];
        BlockedChannels = blockedChannels ?? [];
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
