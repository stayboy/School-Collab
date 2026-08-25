namespace SchoolCollab.Core.Messaging;

/// <summary>
/// Consumer-side counterpart to <see cref="IIntegrationEventPublisher"/>:
/// a handler for one integration event type, invoked by
/// <see cref="RabbitMqSubscriberService"/> when a dispatched outbox message
/// arrives on the module's queue. Register implementations per closed event
/// type; the subscriber deserializes the JSON payload, reconstructs the
/// tenant context from the <c>x-tenant-id</c> header (FR-15), and invokes
/// every registered handler for the event within that tenant scope.
/// </summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : class
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}
