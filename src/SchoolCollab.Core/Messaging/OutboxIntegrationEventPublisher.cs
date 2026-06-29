using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SchoolCollab.Core.Messaging;

/// <summary>
/// Default <see cref="IIntegrationEventPublisher"/> implementation. Writes
/// each event into the outbox table owned by <typeparamref name="TContext"/>
/// inside the current scope's database transaction, so the event is delivered
/// if and only if the originating domain change commits (transactional
/// outbox pattern).
/// </summary>
/// <remarks>
/// The publisher is registered as a singleton and uses
/// <see cref="IDbContextFactory{TContext}"/> to create a short-lived
/// <typeparamref name="TContext"/> per call. This is the EF Core recommended
/// pattern for singleton-scoped consumers and avoids captive-dependency
/// issues. Register the factory once via
/// <c>services.AddDbContextFactory&lt;TContext&gt;(...)</c>.
/// </remarks>
/// <typeparam name="TContext">
/// The bounded-context <see cref="DbContext"/> that owns the
/// <c>outbox_messages</c> table.
/// </typeparam>
public sealed class OutboxIntegrationEventPublisher<TContext> : IIntegrationEventPublisher
    where TContext : DbContext
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly ILogger<OutboxIntegrationEventPublisher<TContext>> _logger;

    /// <summary>
    /// Creates a new publisher bound to the supplied DbContext factory.
    /// </summary>
    public OutboxIntegrationEventPublisher(
        IDbContextFactory<TContext> contextFactory,
        ILogger<OutboxIntegrationEventPublisher<TContext>> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnqueueAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(message);

        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Type = message.GetType().FullName ?? message.GetType().Name,
            Payload = JsonSerializer.Serialize(message, SerializerOptions),
        };

        dbContext.Set<OutboxMessage>().Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Outbox enqueued {EventType} {EventId} for later publication",
            row.Type, row.Id);
    }
}
