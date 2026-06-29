namespace SchoolCollab.Core.Data.Outbox;

/// <summary>
/// Default <see cref="IOutboxConfigurationBuilder"/> implementation.
/// Mutates an <see cref="OutboxConfigurationFlags"/> record via
/// fluent setter methods and returns <c>this</c> for chaining.
/// </summary>
internal sealed class OutboxConfigurationBuilder : IOutboxConfigurationBuilder
{
    private OutboxConfigurationFlags _flags = OutboxConfigurationFlags.Default;

    /// <summary>
    /// Returns the immutable flags snapshot after the fluent
    /// setters have run. Called by
    /// <see cref="OutboxConfigurationFlags.FromConfiguration"/>.
    /// </summary>
    internal OutboxConfigurationFlags Build() => _flags;

    public IOutboxConfigurationBuilder SetTypeMaxLength(int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        _flags = _flags with { TypeMaxLength = maxLength };
        return this;
    }

    public IOutboxConfigurationBuilder UseJsonbPayload()
    {
        _flags = _flags with { PayloadColumnType = "jsonb" };
        return this;
    }

    public IOutboxConfigurationBuilder UseAttemptsDefaultZero()
    {
        _flags = _flags with { AttemptsDefaultValue = 0 };
        return this;
    }

    public IOutboxConfigurationBuilder UsePartialIndexOnOccurredAt()
    {
        _flags = _flags with { UsePartialIndex = true };
        return this;
    }
}