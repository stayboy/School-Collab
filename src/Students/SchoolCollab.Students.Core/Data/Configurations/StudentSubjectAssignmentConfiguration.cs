using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). Inherits the student's
/// tenant; the unique index is composite
/// <c>(tenant_id, student_id, subject_id, period_id)</c>.
/// </summary>
internal sealed class StudentSubjectAssignmentConfiguration : TenantEntityTypeConfigurationBase<StudentSubjectAssignment>
{
    public StudentSubjectAssignmentConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<StudentSubjectAssignment> builder)
    {
        builder.ToTable("student_subject_assignments");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.TopicId).IsRequired();
        builder.Property(x => x.PeriodId).IsRequired();

        builder.Property(x => x.IsOverride)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.SourceType)
            .IsRequired()
            .HasDefaultValue(SubjectAssignmentSource.GradeAssignment);

        builder.HasIndex(x => new { x.TenantId, x.StudentId, x.TopicId, x.PeriodId })
            .IsUnique()
            .HasDatabaseName("ix_student_subject_assignments_tenant_unique");

        builder.HasIndex(x => new { x.TenantId, x.PeriodId })
            .HasDatabaseName("ix_student_subject_assignments_tenant_period");

        builder.Ignore(x => x.DomainEvents);
    }
}
