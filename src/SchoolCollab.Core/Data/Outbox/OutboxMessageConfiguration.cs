using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Core.Data.Outbox;

/// <summary>
/// Shared EF Core mapping for the transactional outbox
/// <c>outbox_messages</c> table. Applies the common shape (table
/// name, column nullability, default indexes) in
/// <see cref="ConfigureEntity"/>; per-module deltas are expressed
/// via the supplied <see cref="OutboxConfigurationFlags"/> passed
/// to the constructor.
/// </summary>
/// <remarks>
/// This class intentionally lives in the shared kernel rather than
/// in any one <c>&lt;Domain&gt;.Core</c> project. Each module applies
/// it via <c>modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(flags))</c>
/// in its <c>OnModelCreating</c>, with the flags coming from the
/// <see cref="OutboxConfigurationBuilder"/> passed to
/// <c>AddOutbox&lt;TContext&gt;</c>.
/// </remarks>
public sealed class OutboxMessageConfiguration : EntityTypeConfigurationBase<OutboxMessage>
{
    private const string TableName = "outbox_messages";
    private const string DispatchedAtIndexName = "ix_outbox_messages_dispatched_at";
    private const string OccurredAtIndexName = "ix_outbox_messages_occurred_at";
    private const string PendingIndexName = "ix_outbox_messages_pending";

    private readonly OutboxConfigurationFlags _flags;

    /// <summary>
    /// Creates a new configuration bound to the supplied
    /// <paramref name="flags"/>.
    /// </summary>
    public OutboxMessageConfiguration(OutboxConfigurationFlags flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        _flags = flags;
    }

    /// <summary>
    /// Convenience constructor using the default flags. Suitable for
    /// modules that do not need any per-domain customisation
    /// (Students today, Assignments after the shared field renames).
    /// </summary>
    public OutboxMessageConfiguration() : this(OutboxConfigurationFlags.Default) { }

    /// <inheritdoc />
    protected override void ConfigureEntity(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable(TableName);

        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Type)
            .HasMaxLength(_flags.TypeMaxLength)
            .IsRequired();

        // Payload: required, with optional column type override
        // (e.g. "jsonb" on PostgreSQL).
        var payload = builder.Property(x => x.Payload).IsRequired();
        if (!string.IsNullOrEmpty(_flags.PayloadColumnType))
        {
            payload.HasColumnType(_flags.PayloadColumnType);
        }

        builder.Property(x => x.DispatchedAt);
        var attempts = builder.Property(x => x.Attempts).IsRequired();
        if (_flags.AttemptsDefaultValue.HasValue)
        {
            attempts.HasDefaultValue(_flags.AttemptsDefaultValue.Value);
        }

        builder.Property(x => x.LastError);

        if (_flags.UsePartialIndex)
        {
            // Single partial index for the dispatcher's pending-rows query.
            builder.HasIndex(x => x.OccurredAt)
                .HasDatabaseName(PendingIndexName)
                .HasFilter("dispatched_at IS NULL");
        }
        else
        {
            // Default: non-filtered indexes on both columns.
            builder.HasIndex(x => x.DispatchedAt)
                .HasDatabaseName(DispatchedAtIndexName);
            builder.HasIndex(x => x.OccurredAt)
                .HasDatabaseName(OccurredAtIndexName);
        }
    }
}
