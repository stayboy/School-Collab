using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). Append-only transfer
/// audit row — never updated or deleted, so no row version is mapped.
/// </summary>
internal sealed class StudentTransferAuditEntryConfiguration : TenantEntityTypeConfigurationBase<StudentTransferAuditEntry>
{
    public StudentTransferAuditEntryConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<StudentTransferAuditEntry> builder)
    {
        builder.ToTable("student_transfer_audit_entries");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.FromGradeLevelId).IsRequired();
        builder.Property(x => x.ToGradeLevelId).IsRequired();
        builder.Property(x => x.PeriodId).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired(false);
        builder.Property(x => x.ActorId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ActorDisplayName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.OccurredAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.StudentId })
            .HasDatabaseName("ix_student_transfer_audit_tenant_student");
        builder.HasIndex(x => new { x.TenantId, x.PeriodId })
            .HasDatabaseName("ix_student_transfer_audit_tenant_period");
        builder.HasIndex(x => new { x.TenantId, x.ToGradeLevelId })
            .HasDatabaseName("ix_student_transfer_audit_tenant_to_grade");
    }
}
