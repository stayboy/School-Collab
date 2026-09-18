namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// E3 (ar-19) — thrown by <c>SweepNotificationQueuer</c> when a sweep asks to render a
/// kind that has no message template. Typed (never <see cref="InvalidOperationException"/>)
/// so a sweep bug surfaces as a named, reviewable exception rather than a 500-shaped throw.
/// </summary>
public sealed class NotificationKindNotSupportedException(string message) : Exception(message);
