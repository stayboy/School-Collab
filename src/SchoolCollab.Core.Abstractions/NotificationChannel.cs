using System.ComponentModel;

namespace SchoolCollab.Core.Notifications;

/// <summary>
/// A delivery channel a tenant may configure for assignment notifications.
/// Shared across bounded contexts (Settings policy + Students policy + Assignments
/// publish-resolution) so the policy layer never depends on a context-specific
/// contact enum. Ordering of values is significant: lower = preferred first.
/// </summary>
public enum NotificationChannel
{
    [Description("Email")] Email = 0,
    [Description("SMS")] SMS = 1,
    [Description("WhatsApp")] WhatsApp = 2,
}
