using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Students.Core.Messaging;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.DispatchedAt);
        builder.Property(x => x.Attempts).IsRequired();
        builder.Property(x => x.LastError);

        builder.HasIndex(x => x.DispatchedAt)
            .HasDatabaseName("ix_outbox_messages_dispatched_at");

        builder.HasIndex(x => x.OccurredAt)
            .HasDatabaseName("ix_outbox_messages_occurred_at");
    }
}