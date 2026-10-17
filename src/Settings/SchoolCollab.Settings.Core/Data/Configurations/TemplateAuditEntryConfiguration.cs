using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

/// <summary>
/// Append-only audit log. No soft-delete, no row-version (the row is never
/// updated after insert). Only the audit timestamps are mapped.
/// TenantId is nullable for global mutations; a named "Tenant" filter is NOT
/// applied (mirrors FlagAuditEntry).
/// </summary>
internal sealed class TemplateAuditEntryConfiguration : EntityTypeConfigurationBase<TemplateAuditEntry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TemplateAuditEntry> builder)
    {
        builder.ToTable("template_audit_entries");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.TenantId);

        builder.Property(x => x.TemplateId).IsRequired();
        builder.Property(x => x.TemplateKey)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.ChangeKind)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(x => x.PreviousSubject).HasMaxLength(500);
        builder.Property(x => x.NewSubject).HasMaxLength(500);
        builder.Property(x => x.PreviousBody).HasMaxLength(4000);
        builder.Property(x => x.NewBody).HasMaxLength(4000);
        builder.Property(x => x.PreviousConstraints).HasMaxLength(2000);
        builder.Property(x => x.NewConstraints).HasMaxLength(2000);
        builder.Property(x => x.Reason).HasMaxLength(1000);

        builder.Property(x => x.ActorId)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(x => x.ActorDisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.OccurredAt).IsRequired();

        builder.HasIndex(x => new { x.TemplateId, x.OccurredAt })
            .HasDatabaseName("ix_template_audit_entries_template_occurred");
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt })
            .HasDatabaseName("ix_template_audit_entries_tenant_occurred");
    }
}