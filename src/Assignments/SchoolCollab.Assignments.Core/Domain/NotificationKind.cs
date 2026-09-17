namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// Why a notification was raised (spec §4 <c>NotificationLog</c>). v1.1 emits only
/// <see cref="Publish"/>; the reminder / completion / overdue kinds are the E3 sweep's
/// contract and exist now so the log schema does not change again for them.
/// </summary>
public enum NotificationKind
{
    /// <summary>Assignment published to the recipient.</summary>
    Publish = 0,

    /// <summary>Reminder for an approaching due date (E3).</summary>
    Reminder = 1,

    /// <summary>Submission / sign-off completed (E3).</summary>
    Completion = 2,

    /// <summary>Overdue notification (E3).</summary>
    Overdue = 3,
}
