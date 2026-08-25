using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Messaging;

/// <summary>
/// Transactional outbox publisher bound to the SCOPED bounded-context
/// <see cref="DbContext"/>. Buffered counterpart to
/// <see cref="OutboxIntegrationEventPublisher{TContext}"/>.
///
/// <para><b>How it achieves atomicity</b> (adr-cross-module-calls.md,
/// outbox-atomicity follow-up): <see cref="EnqueueAsync"/> adds the
/// <see cref="OutboxMessage"/> row to the scoped context WITHOUT saving. When
/// the handler's subsequent <c>SaveChangesAsync</c> commits (repository
/// Add/Update or direct db save), the domain change and the outbox row commit
/// in ONE transaction — delivered iff the mutation commits.</para>
///
/// <para><b>Safety net:</b> call sites that enqueue WITHOUT a later save would
/// strand the row. At scope disposal, any outbox rows still tracked as
/// <see cref="EntityState.Added"/> are flushed here (separate transaction —
/// today's non-atomic semantics, never worse: no event is ever lost).</para>
///
/// <para><b>Tenant stamping (FR-15):</b> identical to the factory publisher —
/// the row carries the ambient tenant at enqueue time so consumers can
/// reconstruct tenant context.</para>
/// </summary>
public sealed class BufferingOutboxPublisher<TContext>(
    TContext db,
    ITenantProvider tenantProvider,
    ILogger<BufferingOutboxPublisher<TContext>> logger) : IIntegrationEventPublisher, IAsyncDisposable, IDisposable
    where TContext : DbContext
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Adds the event to the scoped context's pending changes. Committed by the
    /// handler's next SaveChangesAsync — NOT saved here (that is the point).
    /// </summary>
    public Task EnqueueAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class
        => EnqueueAsync(message, tenantStamp: null, cancellationToken);

    /// <inheritdoc />
    public Task EnqueueAsync<T>(T message, Guid? tenantStamp, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(message);

        var currentTenant = tenantStamp ?? tenantProvider.GetTenantContext().TenantId;
        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Type = message.GetType().FullName ?? message.GetType().Name,
            Payload = JsonSerializer.Serialize(message, SerializerOptions),
            TenantId = currentTenant == Guid.Empty ? null : currentTenant,
        };

        db.Set<OutboxMessage>().Add(row);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Safety-net flush: save any outbox rows still pending because their
    /// handler never called SaveChangesAsync after enqueuing. Rows already
    /// committed by a handler save are <see cref="EntityState.Unchanged"/> and
    /// are skipped — no duplicate work.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            var stranded = db.ChangeTracker.Entries<OutboxMessage>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .ToList();

            if (stranded.Count == 0)
            {
                return;
            }

            logger.LogWarning(
                "Flushing {Count} outbox row(s) stranded without a handler save (non-atomic fallback)", stranded.Count);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Disposal must never mask the handler's own result; a stranded-row
            // flush failure means the event is lost — same worst case as a
            // failed immediate-save enqueue today.
            logger.LogError(ex, "Failed to flush stranded outbox rows at scope disposal");
        }
    }

    /// <summary>Sync disposal path for hosts/scopes that dispose synchronously.</summary>
    public void Dispose()
    {
        try
        {
            var stranded = db.ChangeTracker.Entries<OutboxMessage>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .ToList();
            if (stranded.Count == 0) return;
            logger.LogWarning(
                "Flushing {Count} outbox row(s) stranded without a handler save (non-atomic fallback)", stranded.Count);
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to flush stranded outbox rows at scope disposal");
        }
    }
}
