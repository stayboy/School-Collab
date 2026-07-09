using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Tenancy;

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
    private readonly ITenantProvider _tenantProvider;
    private readonly ILogger<OutboxIntegrationEventPublisher<TContext>> _logger;

    /// <summary>
    /// Creates a new publisher bound to the supplied DbContext factory.
    /// </summary>
    public OutboxIntegrationEventPublisher(
        IDbContextFactory<TContext> contextFactory,
        ITenantProvider tenantProvider,
        ILogger<OutboxIntegrationEventPublisher<TContext>> logger)
    {
        _contextFactory = contextFactory;
        _tenantProvider = tenantProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnqueueAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(message);

        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // FR-15: stamp the publisher's tenant. Guid.Empty (no tenant context) → null
        // (global event). A real tenant → that tenant's id, so the dispatcher/consumer
        // can reconstruct the tenant context before touching tenant-scoped data.
        var currentTenant = _tenantProvider.GetTenantContext().TenantId;

        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Type = message.GetType().FullName ?? message.GetType().Name,
            Payload = JsonSerializer.Serialize(message, SerializerOptions),
            TenantId = currentTenant == Guid.Empty ? null : currentTenant,
        };

        dbContext.Set<OutboxMessage>().Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Outbox enqueued {EventType} {EventId} for later publication",
            row.Type, row.Id);
    }
}
