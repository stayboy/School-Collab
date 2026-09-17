namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// Lifecycle of a <see cref="NotificationLog"/> row.
/// <see cref="Sent"/>, <see cref="Failed"/> and <see cref="Skipped"/> are terminal;
/// <see cref="Failed"/> with a <c>NextRetryAt</c> is retryable until the attempt cap
/// (<see cref="Services.NotificationRetrySchedule.MaxAttempts"/>).
/// </summary>
public enum NotificationDeliveryStatus
{
    /// <summary>Queued for the next drain (or waiting for its <c>NextRetryAt</c>).</summary>
    Queued = 0,

    /// <summary>Delivered (or handed to a log-and-skip sender that reports success).</summary>
    Sent = 1,

    /// <summary>Delivery failed; retryable until the attempt cap, then terminal.</summary>
    Failed = 2,

    /// <summary>Deliberately not sent (no deep-link token / expired / no address). Never delivered.</summary>
    Skipped = 3,
}
