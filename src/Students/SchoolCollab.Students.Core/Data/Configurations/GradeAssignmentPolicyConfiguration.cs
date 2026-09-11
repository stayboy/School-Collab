using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Optional per-grade guardian-signature policy. Strict tenant-scoped, one row per
/// (tenant, grade). Null override = inherit the tenant default. Cascade-deletes
/// with its grade level (WS-C1 / spec §7 Q1).
/// </summary>
internal sealed class GradeAssignmentPolicyConfiguration
    : TenantEntityTypeConfigurationBase<GradeAssignmentPolicy>
{
    public GradeAssignmentPolicyConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<GradeAssignmentPolicy> builder)
    {
        builder.ToTable("grade_assignment_policies");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.GradeLevelId).IsRequired();
        builder.Property(x => x.RequiresSignatureDefault);

        // One policy row per (tenant, grade); cascade-delete with the grade.
        builder.HasOne<GradeLevel>()
            .WithMany()
            .HasForeignKey(x => x.GradeLevelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.GradeLevelId })
            .IsUnique()
            .HasDatabaseName("ix_grade_assignment_policies_tenant_grade")
            .HasFilter("is_deleted = false");
    }
}
