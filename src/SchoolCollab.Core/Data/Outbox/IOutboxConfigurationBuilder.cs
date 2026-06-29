namespace SchoolCollab.Core.Data.Outbox;

/// <summary>
/// Fluent builder used by a <c>&lt;Domain&gt;.Core</c> to customise
/// the shared <see cref="OutboxConfigurationFlags"/> when calling
/// <c>AddOutbox&lt;TContext&gt;</c>. The default flags cover the
/// most common case; only override what is genuinely per-domain.
/// </summary>
public interface IOutboxConfigurationBuilder
{
    /// <summary>
    /// Sets the <c>Type</c> column max length. Default 200.
    /// </summary>
    IOutboxConfigurationBuilder SetTypeMaxLength(int maxLength);

    /// <summary>
    /// Switches the <c>Payload</c> column to PostgreSQL <c>jsonb</c>.
    /// </summary>
    IOutboxConfigurationBuilder UseJsonbPayload();

    /// <summary>
    /// Sets a database default of <c>0</c> on the <c>Attempts</c> column.
    /// </summary>
    IOutboxConfigurationBuilder UseAttemptsDefaultZero();

    /// <summary>
    /// Replaces the default non-filtered indexes with a single partial
    /// index on <c>OccurredAt WHERE DispatchedAt IS NULL</c>.
    /// </summary>
    IOutboxConfigurationBuilder UsePartialIndexOnOccurredAt();
}
