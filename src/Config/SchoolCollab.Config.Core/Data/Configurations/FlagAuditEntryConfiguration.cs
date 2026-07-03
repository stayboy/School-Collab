using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Config.Core.Domain;

namespace SchoolCollab.Config.Core.Data.Configurations;

/// <summary>
/// Append-only audit log. No soft-delete, no row-version (the row is never
/// updated after insert). Only the audit timestamps are mapped.
/// </summary>
internal sealed class FlagAuditEntryConfiguration : EntityTypeConfigurationBase<FlagAuditEntry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<FlagAuditEntry> builder)
    {
        builder.ToTable("flag_audit_entries");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.TenantId);

        builder.Property(x => x.FeatureFlagId).IsRequired();

        builder.Property(x => x.FeatureFlagKey)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.ChangeKind)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(x => x.PreviousIsEnabled);
        builder.Property(x => x.NewIsEnabled);
        builder.Property(x => x.Reason).HasMaxLength(1000);

        builder.Property(x => x.ActorId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.ActorDisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.OccurredAt).IsRequired();

        builder.HasIndex(x => new { x.FeatureFlagId, x.OccurredAt })
            .HasDatabaseName("ix_flag_audit_entries_flag_occurred");

        builder.HasIndex(x => new { x.TenantId, x.OccurredAt })
            .HasDatabaseName("ix_flag_audit_entries_tenant_occurred");
    }
}