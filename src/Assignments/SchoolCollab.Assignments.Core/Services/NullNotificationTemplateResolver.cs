using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// The Core-side default <see cref="INotificationTemplateResolver"/>: reports
/// <see cref="NotificationTemplateSlotState.NotSet"/> always, so every host that builds the
/// broadcaster or the sweep queuer (<c>AddAssignmentsCore</c>) has a working container regardless of
/// wiring. Hosts with the Settings HTTP client (Assignments.Api, Assignments.Worker) re-register
/// <see cref="INotificationTemplateResolver"/> with <see cref="HttpNotificationTemplateResolver"/>
/// after <c>AddAssignmentsCore</c> — the later registration wins, and a host that skips it simply
/// renders the built-in defaults (the fail-open behaviour, by construction).
/// </summary>
public sealed class NullNotificationTemplateResolver : INotificationTemplateResolver
{
    /// <inheritdoc />
    public Task<NotificationTemplateCandidatesRead> ReadCandidatesAsync(
        NotificationKind kind,
        NotificationChannel channel,
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(NotificationTemplateCandidatesRead.NotSet);
}
