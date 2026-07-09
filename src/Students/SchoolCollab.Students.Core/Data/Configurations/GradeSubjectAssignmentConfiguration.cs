using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). Tenant-owned; the unique
/// index is composite <c>(tenant_id, grade_level_id, subject_id, period_id)</c>.
/// </summary>
internal sealed class GradeSubjectAssignmentConfiguration : TenantEntityTypeConfigurationBase<GradeSubjectAssignment>
{
    public GradeSubjectAssignmentConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<GradeSubjectAssignment> builder)
    {
        builder.ToTable("grade_subject_assignments");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.GradeLevelId).IsRequired();
        builder.Property(x => x.SubjectId).IsRequired();
        builder.Property(x => x.PeriodId).IsRequired();
        builder.Property(x => x.SubjectStrandId);
        builder.Property(x => x.SubjectLessonId);

        builder.HasOne<SubjectStrand>()
            .WithMany()
            .HasForeignKey(x => x.SubjectStrandId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<SubjectLesson>()
            .WithMany()
            .HasForeignKey(x => x.SubjectLessonId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.TenantId, x.GradeLevelId, x.SubjectId, x.PeriodId })
            .IsUnique()
            .HasDatabaseName("ix_grade_subject_assignments_tenant_unique");

        builder.HasIndex(x => new { x.TenantId, x.PeriodId })
            .HasDatabaseName("ix_grade_subject_assignments_tenant_period");

        builder.Ignore(x => x.DomainEvents);
    }
}
