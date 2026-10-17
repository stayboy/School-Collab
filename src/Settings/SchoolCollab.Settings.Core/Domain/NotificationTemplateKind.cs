namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// The notification kind a template applies to (spec §3.2 / §5). Mirrors
/// <see cref="SchoolCollab.Assignments.Core.Domain.NotificationKind"/> but is declared
/// locally so Settings.Core has no reference to Assignments.Core (cross-context boundary).
/// </summary>
public enum NotificationTemplateKind
{
    /// <summary>Assignment published to the recipient.</summary>
    Publish = 0,

    /// <summary>Reminder for an approaching due date (E3).</summary>
    Reminder = 1,

    /// <summary>Submission / sign-off completed (E3).</summary>
    Completion = 2,

    /// <summary>Overdue notification (E3).</summary>
    Overdue = 3
}