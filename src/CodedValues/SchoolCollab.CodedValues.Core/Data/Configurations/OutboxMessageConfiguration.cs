using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.CodedValues.Core.Messaging;

namespace SchoolCollab.CodedValues.Core.Data.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Type).IsRequired().HasMaxLength(500);
        builder.Property(x => x.Payload).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.DispatchedAt);
        builder.Property(x => x.Attempts).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.LastError);

        // Partial index keeps the dispatcher's SELECT cheap as the table grows
        // and old dispatched rows accumulate.
        builder.HasIndex(x => x.OccurredAt)
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("dispatched_at IS NULL");
    }
}
