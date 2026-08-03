using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). Tenant-owned; the unique
/// index is composite <c>(tenant_id, grade_level_id, activity_group_id, topic_id)</c>.
/// The assignment's effective period is date-based (<c>start_date</c> /
/// <c>end_date</c>), not period-bound, so a topic can stay assigned to a grade
/// across multiple years unless blocked or archived (a set <c>end_date</c>).
/// </summary>
internal sealed class GradeSubjectAssignmentConfiguration : TenantEntityTypeConfigurationBase<GradeSubjectAssignment>
{
    public GradeSubjectAssignmentConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<GradeSubjectAssignment> builder)
    {
        builder.ToTable("grade_subject_assignments");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.GradeLevelId);
        builder.Property(x => x.ActivityGroupId);
        builder.Property(x => x.TopicId).IsRequired();
        builder.Property(x => x.StartDate).IsRequired();
        builder.Property(x => x.EndDate);
        builder.Property(x => x.TopicStrandId);
        builder.Property(x => x.TopicLessonId);

        builder.HasOne<TopicStrand>()
            .WithMany()
            .HasForeignKey(x => x.TopicStrandId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<TopicLesson>()
            .WithMany()
            .HasForeignKey(x => x.TopicLessonId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.TenantId, x.GradeLevelId, x.ActivityGroupId, x.TopicId })
            .IsUnique()
            .HasDatabaseName("ix_grade_subject_assignments_tenant_unique");

        builder.HasIndex(x => new { x.TenantId, x.StartDate, x.EndDate })
            .HasDatabaseName("ix_grade_subject_assignments_tenant_effective_dates");

        builder.Ignore(x => x.DomainEvents);
    }
}
