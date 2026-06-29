using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Students.Core.Data;

namespace SchoolCollab.Students.Core.Messaging;

/// <summary>
/// Drains <see cref="OutboxMessage"/> rows to RabbitMQ. Polls on a short
/// interval, uses <c>FOR UPDATE SKIP LOCKED</c> so multiple instances can run
/// safely, and only marks a row dispatched after the broker confirms
/// (persistent delivery + publisher confirms).
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IConnection connection,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const string ExchangeName = "students";
    private const int BatchSize = 100;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxDispatcher started; exchange={Exchange}", ExchangeName);

        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true,
            outstandingPublisherConfirmationsRateLimiter: null);

        await using var channel = await connection.CreateChannelAsync(channelOptions, stoppingToken);

        await channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchBatchAsync(channel, stoppingToken);
                if (dispatched == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxDispatcher loop failed; will retry after delay");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }

        logger.LogInformation("OutboxDispatcher stopping");
    }

    private async Task<int> DispatchBatchAsync(IChannel channel, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();

        await using var tx = await dbContext.Database.BeginTransactionAsync(stoppingToken);

        var batch = await dbContext.OutboxMessages
            .FromSqlRaw(
                """
                SELECT * FROM outbox_messages
                WHERE dispatched_at IS NULL
                ORDER BY occurred_at
                LIMIT {0}
                FOR UPDATE SKIP LOCKED
                """, BatchSize)
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
                    exchange: ExchangeName,
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
                logger.LogWarning(ex,
                    "Outbox publish failed for {EventType} {EventId} (attempt {Attempts})",
                    msg.Type, msg.Id, msg.Attempts);
            }
        }

        await dbContext.SaveChangesAsync(stoppingToken);
        await tx.CommitAsync(stoppingToken);

        var succeeded = batch.Count(m => m.DispatchedAt is not null);
        logger.LogDebug(
            "Outbox batch dispatched {Succeeded}/{Total}",
            succeeded, batch.Count);

        return succeeded;
    }
}