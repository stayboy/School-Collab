using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). Rev. 2 eligible-grade
/// link table; both FKs cascade (removing a group or a grade removes its links).
/// </summary>
internal sealed class ActivityGroupGradeLevelConfiguration : TenantEntityTypeConfigurationBase<ActivityGroupGradeLevel>
{
    public ActivityGroupGradeLevelConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<ActivityGroupGradeLevel> builder)
    {
        builder.ToTable("activity_group_grade_levels");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.ActivityGroupId).IsRequired();
        builder.Property(x => x.GradeLevelId).IsRequired();

        builder.HasOne<ActivityGroup>()
            .WithMany()
            .HasForeignKey(x => x.ActivityGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<GradeLevel>()
            .WithMany()
            .HasForeignKey(x => x.GradeLevelId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique per (tenant, group, grade) — a grade is in the eligible set once.
        builder.HasIndex(x => new { x.TenantId, x.ActivityGroupId, x.GradeLevelId })
            .IsUnique()
            .HasDatabaseName("ix_agg_tenant_group_grade_unique");

        builder.HasIndex(x => new { x.TenantId, x.GradeLevelId })
            .HasDatabaseName("ix_agg_tenant_grade");
    }
}