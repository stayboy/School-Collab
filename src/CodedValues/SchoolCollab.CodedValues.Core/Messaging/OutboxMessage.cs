using SchoolCollab.Core.Data;

namespace SchoolCollab.CodedValues.Core.Messaging;

/// <summary>
/// A single outbox row. Created in the same database transaction as the domain
/// change that produced it. The <see cref="OutboxDispatcher"/> reads pending
/// rows, publishes them to RabbitMQ, and sets <see cref="DispatchedAt"/>.
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
}
