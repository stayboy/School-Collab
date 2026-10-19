using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// The Core-side default <see cref="INotificationTemplateResolver"/>: resolves
/// <see langword="null"/> always, so every host that builds the broadcaster
/// (<see cref="AssignmentNotificationBroadcaster"/> — Api and Worker both call
/// <c>AddAssignmentsCore</c>) has a working container regardless of wiring. Hosts with
/// the Settings HTTP client (Assignments.Api, Assignments.Worker) re-register
/// <see cref="INotificationTemplateResolver"/> with
/// <see cref="HttpNotificationTemplateResolver"/> after <c>AddAssignmentsCore</c> — the
/// later registration wins, and a host that skips it simply renders built-in defaults
/// (the fail-open behaviour, by construction). Reviewer MAJOR-1 fix (Worker DI gap).
/// </summary>
public sealed class NullNotificationTemplateResolver : INotificationTemplateResolver
{
    /// <inheritdoc />
    public Task<ResolvedNotificationTemplate?> ResolveAsync(
        NotificationKind kind,
        NotificationChannel channel,
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<ResolvedNotificationTemplate?>(null);

    /// <inheritdoc />
    public Task<NotificationTemplateSlot?> ResolveSlotAsync(
        NotificationKind kind,
        NotificationChannel channel,
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<NotificationTemplateSlot?>(null);

    /// <inheritdoc />
    public Task<NotificationTemplateSlotRead> ReadSlotAsync(
        NotificationKind kind,
        NotificationChannel channel,
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new NotificationTemplateSlotRead(NotificationTemplateSlotState.NotSet, null));
}
