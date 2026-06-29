namespace SchoolCollab.Core.Messaging;

/// <summary>
/// Configuration for the transactional outbox dispatcher that drains
/// <see cref="OutboxMessage"/> rows to RabbitMQ. Bound from the
/// <see cref="SectionName"/> configuration section by
/// <see cref="OutboxExtensions.AddOutbox{TContext}"/>.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>
    /// Default configuration section name: <c>Outbox</c>.
    /// </summary>
    public const string SectionName = "Outbox";

    /// <summary>
    /// The RabbitMQ topic exchange that dispatched events are published to.
    /// Each bounded context uses its own exchange (e.g. <c>students</c>,
    /// <c>coded-values</c>, <c>assignments</c>) so consumers can subscribe
    /// to a specific module's events.
    /// </summary>
    public string ExchangeName { get; set; } = default!;

    /// <summary>
    /// Maximum number of outbox rows claimed in a single
    /// <c>FOR UPDATE SKIP LOCKED</c> batch. Default 100.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Idle poll interval between empty batches. Default 1 second.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
}
