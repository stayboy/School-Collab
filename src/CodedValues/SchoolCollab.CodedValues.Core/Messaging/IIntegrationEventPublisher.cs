namespace SchoolCollab.CodedValues.Core.Messaging;

/// <summary>
/// Append-only publisher for integration events. Implementations must persist
/// the event in the same database transaction as the originating domain change
/// so the event is delivered if and only if the change commits (transactional
/// outbox pattern).
/// </summary>
public interface IIntegrationEventPublisher
{
    Task EnqueueAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class;
}
