using SchoolCollab.Core.Data;

namespace SchoolCollab.Core.Messaging;

/// <summary>
/// A single outbox row. Created in the same database transaction as the domain
/// change that produced it. The outbox dispatcher reads pending rows, publishes
/// them to RabbitMQ, and sets <see cref="DispatchedAt"/>.
/// </summary>
public sealed class OutboxMessage : IEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Type { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public DateTimeOffset? DispatchedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    /// <summary>
    /// The publisher's tenant at enqueue time (<c>null</c> = global event).
    /// Carried as payload so the dispatcher/consumer can reconstruct the tenant
    /// context before invoking handlers that touch tenant-scoped data (FR-15).
    /// <see cref="OutboxMessage"/> is a global allow-list entity (no "Tenant"
    /// query filter), so this column is provenance/routing, not a filter key.
    /// </summary>
    public Guid? TenantId { get; set; }
}
