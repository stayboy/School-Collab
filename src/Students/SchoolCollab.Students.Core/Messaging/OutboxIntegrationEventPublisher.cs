using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Students.Core.Data;

namespace SchoolCollab.Students.Core.Messaging;

/// <summary>
/// Writes integration events to the outbox in the current
/// <see cref="StudentsDbContext"/> so the event and the originating domain
/// change share a single database transaction. Does not talk to RabbitMQ —
/// the <see cref="OutboxDispatcher"/> handles delivery.
/// </summary>
internal sealed class OutboxIntegrationEventPublisher(
    StudentsDbContext dbContext,
    ILogger<OutboxIntegrationEventPublisher> logger) : IIntegrationEventPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task EnqueueAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(message);

        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Type = message.GetType().FullName ?? message.GetType().Name,
            Payload = JsonSerializer.Serialize(message, SerializerOptions),
        };

        dbContext.Set<OutboxMessage>().Add(row);

        logger.LogDebug(
            "Outbox enqueued {EventType} {EventId} for later publication",
            row.Type, row.Id);
    }
}