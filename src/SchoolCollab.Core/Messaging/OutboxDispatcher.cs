using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace SchoolCollab.Core.Messaging;

/// <summary>
/// Drains <see cref="OutboxMessage"/> rows owned by <typeparamref name="TContext"/>
/// to RabbitMQ. Polls on a short interval, uses
/// <c>FOR UPDATE SKIP LOCKED</c> so multiple instances can run safely, and
/// only marks a row dispatched after the broker confirms (persistent
/// delivery + publisher confirms).
/// </summary>
/// <remarks>
/// One <see cref="OutboxDispatcher{TContext}"/> instance is registered per
/// bounded context by <see cref="OutboxExtensions.AddOutbox{TContext}"/>.
/// The target RabbitMQ exchange is read from
/// <see cref="OutboxOptions.ExchangeName"/>.
/// </remarks>
/// <typeparam name="TContext">
/// The bounded-context <see cref="DbContext"/> that owns the
/// <c>outbox_messages</c> table.
/// </typeparam>
public sealed class OutboxDispatcher<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnection _connection;
    private readonly IOptionsMonitor<OutboxOptions> _optionsMonitor;
    private readonly ILogger<OutboxDispatcher<TContext>> _logger;

    /// <summary>
    /// Creates a new dispatcher bound to the supplied DbContext type.
    /// </summary>
    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IConnection connection,
        IOptionsMonitor<OutboxOptions> optionsMonitor,
        ILogger<OutboxDispatcher<TContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _connection = connection;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _optionsMonitor.CurrentValue;
        _logger.LogInformation("OutboxDispatcher started; exchange={Exchange}", options.ExchangeName);

        // Publisher confirmations + tracking: the library throws PublishException
        // when a message is nacked or returned, so we don't need to call
        // WaitForConfirms manually.
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true,
            outstandingPublisherConfirmationsRateLimiter: null);

        await using var channel = await _connection.CreateChannelAsync(channelOptions, stoppingToken);

        await channel.ExchangeDeclareAsync(
            exchange: options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchBatchAsync(channel, options, stoppingToken);
                if (dispatched == 0)
                {
                    await Task.Delay(options.PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxDispatcher loop failed; will retry after delay");
                await Task.Delay(options.PollInterval, stoppingToken);
            }
        }

        _logger.LogInformation("OutboxDispatcher stopping");
    }

    private async Task<int> DispatchBatchAsync(IChannel channel, OutboxOptions options, CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();

        await using var tx = await dbContext.Database.BeginTransactionAsync(stoppingToken);

        // FOR UPDATE SKIP LOCKED: another instance is allowed to pick up the
        // rows we don't grab.
        var batch = await dbContext.Set<OutboxMessage>()
            .FromSqlRaw(
                """
                SELECT * FROM outbox_messages
                WHERE dispatched_at IS NULL
                ORDER BY occurred_at
                LIMIT {0}
                FOR UPDATE SKIP LOCKED
                """, options.BatchSize)
            .ToListAsync(stoppingToken);

        if (batch.Count == 0)
        {
            await tx.RollbackAsync(stoppingToken);
            return 0;
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var msg in batch)
        {
            try
            {
                var body = Encoding.UTF8.GetBytes(msg.Payload);
                var props = new BasicProperties
                {
                    ContentType = "application/json",
                    DeliveryMode = DeliveryModes.Persistent,
                    MessageId = msg.Id.ToString(),
                    Type = msg.Type,
                    Timestamp = new AmqpTimestamp(msg.OccurredAt.ToUnixTimeSeconds()),
                };

                await channel.BasicPublishAsync(
                    exchange: options.ExchangeName,
                    routingKey: msg.Type,
                    mandatory: true,
                    basicProperties: props,
                    body: body,
                    cancellationToken: stoppingToken);

                msg.DispatchedAt = now;
                msg.LastError = null;
            }
            catch (Exception ex)
            {
                msg.Attempts += 1;
                msg.LastError = ex.Message;
                _logger.LogWarning(ex,
                    "Outbox publish failed for {EventType} {EventId} (attempt {Attempts})",
                    msg.Type, msg.Id, msg.Attempts);
            }
        }

        await dbContext.SaveChangesAsync(stoppingToken);
        await tx.CommitAsync(stoppingToken);

        var succeeded = batch.Count(m => m.DispatchedAt is not null);
        _logger.LogDebug(
            "Outbox batch dispatched {Succeeded}/{Total}",
            succeeded, batch.Count);

        return succeeded;
    }
}
