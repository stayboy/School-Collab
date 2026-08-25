namespace SchoolCollab.Core.Messaging;

/// <summary>
/// Append-only publisher for integration events. Implementations must persist
/// the event in the same database transaction as the originating domain change
/// so the event is delivered if and only if the change commits (transactional
/// outbox pattern).
/// </summary>
public interface IIntegrationEventPublisher
{
    /// <summary>Stamps the outbox row with the ambient tenant (null when default/global).</summary>
    Task EnqueueAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Enqueue with an EXPLICIT outbox-row tenant stamp, bypassing the ambient
    /// context. Use when the enqueue happens inside a tenant scope that must not
    /// leak into the row's routing metadata (e.g. a global-config change written
    /// under a target tenant's save-guard).
    /// </summary>
    Task EnqueueAsync<T>(T message, Guid? tenantStamp, CancellationToken cancellationToken = default)
        where T : class;
}
