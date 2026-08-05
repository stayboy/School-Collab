using System.Text.Json.Serialization;

namespace SchoolCollab.Core.Notifications;

/// <summary>
/// Raw, configurable notification-policy fields shared across bounded contexts
/// (notification-delivery-plan.md §2). Used as the input shape for effective-policy
/// resolution: a tenant-global default and an optional per-grade override are both
/// expressed as <see cref="NotificationPolicyFields"/>.
///
/// <para><b>Null semantics differ by role.</b> As a <b>grade override</b>, a null field
/// means "inherit the tenant default"; as the <b>tenant default</b>, a null field means
/// "no tenant value set, built-in default applies at publish time". Channel lists are
/// nullable so "explicitly clear" (empty array) is distinguishable from "inherit" (null).</para>
/// </summary>
public sealed record NotificationPolicyFields
{
    public NotificationChannel[]? PreferredChannelOrder { get; init; }
    public NotificationChannel[]? BlockedChannels { get; init; }
    public int? MaxNotifications { get; init; }
    public int? MaxReminders { get; init; }
    public int? ReminderIntervalHours { get; init; }
    public int? LinkValidityDays { get; init; }
    public TimeOnly? SendoutTimeOfDay { get; init; }
    public int? SendoutIntervalMinutes { get; init; }

    /// <summary>A policy with every field null ("nothing configured").</summary>
    public static NotificationPolicyFields Empty { get; } = new();
}
