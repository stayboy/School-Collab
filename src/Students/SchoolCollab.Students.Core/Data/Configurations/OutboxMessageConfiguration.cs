using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class OutboxMessageConfiguration : EntityTypeConfigurationBase<OutboxMessage>
{
    protected override void ConfigureEntity(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
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