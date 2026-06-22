using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Messaging;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

internal sealed class OutboxMessageConfiguration : EntityTypeConfigurationBase<OutboxMessage>
{
    protected override void ConfigureEntity(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.Property(x => x.Type).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Payload).IsRequired();
        builder.HasIndex(x => x.ProcessedAt)
            .HasDatabaseName("ix_outbox_messages_processed_at")
            .HasFilter("processed_at IS NULL");
    }
}