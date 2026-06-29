namespace SchoolCollab.Core.Data.Outbox;

/// <summary>
/// Per-module configuration flags for the shared
/// <c>OutboxMessageConfigurationBase</c>. Defaults match the
/// common-case (Students today; Assignments after the
/// <see cref="OutboxMessage"/> field renames land). CodedValues
/// overrides via the fluent <see cref="IOutboxConfigurationBuilder"/>
/// to set <c>jsonb</c> on the payload column, a larger
/// <see cref="TypeMaxLength"/>, a <c>0</c> default on
/// <see cref="AttemptsDefaultValue"/>, and a partial index on
/// <c>OccurredAt</c>.
/// </summary>
/// <param name="TypeMaxLength">
/// Maximum length of the <c>Type</c> column. Default 200
/// (matches the existing Students and Assignments configurations).
/// </param>
/// <param name="PayloadColumnType">
/// Optional column type override for the <c>Payload</c> column.
/// Default <c>null</c> means the database's default text type
/// (e.g. <c>text</c> on PostgreSQL). Set to <c>jsonb</c> to use
/// PostgreSQL's binary JSON column type.
/// </param>
/// <param name="AttemptsDefaultValue">
/// Optional database default for the <c>Attempts</c> column.
/// Default <c>null</c> means no database default; the application
/// sets the value on every insert. Set to <c>0</c> to let the
/// database apply the default.
/// </param>
/// <param name="UsePartialIndex">
/// When <c>true</c>, the default non-filtered indexes on
/// <c>DispatchedAt</c> and <c>OccurredAt</c> are replaced with a
/// single partial index on <c>OccurredAt WHERE DispatchedAt IS NULL</c>.
/// This matches the existing CodedValues configuration and keeps
/// the dispatcher's <c>SELECT</c> cheap as old dispatched rows
/// accumulate.
/// </param>
public sealed record OutboxConfigurationFlags(
    int TypeMaxLength = 200,
    string? PayloadColumnType = null,
    int? AttemptsDefaultValue = null,
    bool UsePartialIndex = false)
{
    /// <summary>
    /// The default flags, matching the most common case (Students,
    /// and Assignments after the field renames).
    /// </summary>
    public static OutboxConfigurationFlags Default { get; } = new();

    /// <summary>
    /// Builds the immutable flags snapshot after the optional
    /// <paramref name="configure"/> callback has run on a fresh
    /// <see cref="IOutboxConfigurationBuilder"/>. Pass
    /// <paramref name="configure"/> as <c>null</c> to receive
    /// <see cref="Default"/>.
    /// </summary>
    /// <remarks>
    /// This is the public entry point used by both
    /// <c>AddOutbox&lt;TContext&gt;</c> at runtime and by the
    /// design-time <c>DbContext</c> factory at migration time.
    /// The internal builder type stays internal because callers
    /// only ever interact through the public interface.
    /// </remarks>
    public static OutboxConfigurationFlags FromConfiguration(
        Action<IOutboxConfigurationBuilder>? configure)
    {
        if (configure is null)
        {
            return Default;
        }

        var builder = new OutboxConfigurationBuilder();
        configure(builder);
        return builder.Build();
    }
}