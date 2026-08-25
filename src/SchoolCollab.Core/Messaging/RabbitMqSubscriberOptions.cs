namespace SchoolCollab.Core.Messaging;

/// <summary>
/// Configuration for the RabbitMQ integration-event subscriber.
/// Bound from the <see cref="SectionName"/> configuration section
/// (<c>RabbitMq:Subscriber</c>).
/// </summary>
public sealed class RabbitMqSubscriberOptions
{
    /// <summary>Default configuration section name.</summary>
    public const string SectionName = "RabbitMq:Subscriber";

    /// <summary>
    /// The upstream module's topic exchange to consume from (e.g. the
    /// Settings exchange for coded-value projection consumers). Declared
    /// idempotently with the same shape the outbox dispatcher uses
    /// (topic, durable) so first-consumer startup works even if the
    /// publisher has not run yet.
    /// </summary>
    public string ExchangeName { get; set; } = default!;

    /// <summary>
    /// This consumer's durable queue. Distinct per consuming service so
    /// each gets its own copy of every matched message (competing consumers
    /// of one queue share deliveries instead).
    /// </summary>
    public string QueueName { get; set; } = default!;
}
