using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Messaging;

/// <summary>
/// Consumes dispatched outbox messages from a RabbitMQ topic exchange and
/// dispatches them to registered <see cref="IIntegrationEventHandler{TEvent}"/>
/// implementations. The consume-side counterpart to
/// <see cref="OutboxDispatcher{TContext}"/>.
///
/// <para><b>Tenant reconstruction (FR-15):</b> handlers run inside
/// <c>ITenantContextAccessor.RunWithExplicitTenantAsync</c> using the
/// publisher's tenant carried in the <c>x-tenant-id</c> header; global events
/// (no header / null tenant) run under the default context.</para>
///
/// <para><b>Ack policy:</b> a message is acked after all its handlers succeed.
/// On handler failure the message is nacked WITHOUT requeue (a poison message
/// would otherwise loop forever against the same handler). Failures are logged
/// with full event details; reference-data projections recover via later events
/// or the backfill job — see adr-cross-module-calls.md Phase 1.</para>
/// </summary>
public sealed class RabbitMqSubscriberService : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<RabbitMqSubscriberOptions> _optionsMonitor;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly ILogger<RabbitMqSubscriberService> _logger;

    // Event type full name → CLR type. Built once from the event types passed
    // to AddRabbitMqSubscriber; routing keys bind on these same names because
    // the dispatcher publishes with routingKey = msg.Type = type FullName.
    private readonly ImmutableDictionary<string, Type> _eventTypes;

    public RabbitMqSubscriberService(
        IConnection connection,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<RabbitMqSubscriberOptions> optionsMonitor,
        ITenantContextAccessor tenantContextAccessor,
        RabbitMqEventTypes eventTypes,
        ILogger<RabbitMqSubscriberService> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _optionsMonitor = optionsMonitor;
        _tenantContextAccessor = tenantContextAccessor;
        _logger = logger;
        _eventTypes = eventTypes.EventTypes.ToImmutableDictionary(t => t.FullName ?? t.Name);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _optionsMonitor.CurrentValue;
        _logger.LogInformation(
            "RabbitMqSubscriber started; exchange={Exchange} queue={Queue} events={Count}",
            options.ExchangeName, options.QueueName, _eventTypes.Count);

        await using var channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Same declaration as OutboxDispatcher so binding never fails on a
        // not-yet-declared exchange.
        await channel.ExchangeDeclareAsync(
            exchange: options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        foreach (var typeName in _eventTypes.Keys)
        {
            await channel.QueueBindAsync(
                queue: options.QueueName,
                exchange: options.ExchangeName,
                routingKey: typeName,
                cancellationToken: stoppingToken);
        }

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                await HandleDeliveryAsync(ea, stoppingToken);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Integration event {EventType} ({MessageId}) failed; nacking without requeue",
                    ea.BasicProperties.Type, ea.BasicProperties.MessageId);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        await channel.BasicConsumeAsync(
            queue: options.QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        // BasicConsumeAsync keeps the channel alive; park until shutdown.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleDeliveryAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var typeName = ea.BasicProperties.Type;
        if (typeName is null || !_eventTypes.TryGetValue(typeName, out var eventType))
        {
            _logger.LogWarning("No handler binding for integration event {EventType}; ignoring", typeName);
            return;
        }

        var @event = JsonSerializer.Deserialize(ea.Body.ToArray(), eventType, SerializerOptions)
            ?? throw new InvalidOperationException($"Payload for {typeName} deserialized to null.");

        // x-tenant-id header: 16-byte little-endian Guid written by the dispatcher.
        Guid? tenantId = null;
        if (ea.BasicProperties.Headers?.TryGetValue("x-tenant-id", out var raw) == true
            && raw is byte[] bytes
            && bytes.Length == 16)
        {
            tenantId = new Guid(bytes);
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var handlerInterface = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var handlers = scope.ServiceProvider.GetServices(handlerInterface).ToArray();

        if (handlers.Length == 0)
        {
            _logger.LogWarning("No registered handlers for {EventType}; acknowledging", typeName);
            return;
        }

        var handleMethod = handlerInterface.GetMethod(nameof(IIntegrationEventHandler<object>.HandleAsync))!;

        // Reconstruct the publisher's tenant before invoking handlers (FR-15).
        await _tenantContextAccessor.RunWithExplicitTenantAsync<object?>(
            tenantId,
            async innerCt =>
            {
                foreach (var handler in handlers)
                {
                    await (Task)handleMethod.Invoke(handler, [@event, innerCt])!;
                }

                return null;
            },
            ct);

        _logger.LogDebug("Handled {EventType} by {HandlerCount} handler(s)", typeName, handlers.Length);
    }
}
