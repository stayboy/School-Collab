using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeNotificationPolicies.Commands.UpsertGradeNotificationPolicy;

/// <summary>
/// Creates or replaces the current tenant's override policy for a grade. A null field
/// is stored as null = "inherit the tenant default" (so clearing an existing override
/// value means setting it back to null). Returns the persisted override DTO.
/// </summary>
public sealed record UpsertGradeNotificationPolicy(
    Guid GradeLevelId,
    NotificationChannel[]? PreferredChannelOrder,
    NotificationChannel[]? BlockedChannels,
    int? MaxNotifications,
    int? MaxReminders,
    int? ReminderIntervalHours,
    int? LinkValidityDays,
    TimeOnly? SendoutTimeOfDay,
    int? SendoutIntervalMinutes) : ICommand;
