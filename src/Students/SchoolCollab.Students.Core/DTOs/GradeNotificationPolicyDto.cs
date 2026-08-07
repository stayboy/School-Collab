using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// Wire shape for a <see cref="Domain.GradeNotificationPolicy"/> (the raw per-grade
/// override). Null fields mean "inherit the tenant default" (nothing stored for this
/// grade). The effective (merged) policy is computed elsewhere by the shared resolver
/// fed with the tenant default + this override.
/// </summary>
public sealed record GradeNotificationPolicyDto(
    Guid GradeLevelId,
    NotificationChannel[]? PreferredChannelOrder,
    NotificationChannel[]? BlockedChannels,
    int? MaxNotifications,
    int? MaxReminders,
    int? ReminderIntervalHours,
    int? LinkValidityDays,
    TimeOnly? SendoutTimeOfDay,
    int? SendoutIntervalMinutes,
    DateTimeOffset UpdatedAt);
