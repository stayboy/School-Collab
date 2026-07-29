using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). Inherits the student's
/// tenant; the unique index is composite <c>(tenant_id, student_id, period_id)</c>.
/// </summary>
internal sealed class StudentEnrollmentConfiguration : TenantEntityTypeConfigurationBase<StudentEnrollment>
{
    public StudentEnrollmentConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<StudentEnrollment> builder)
    {
        builder.ToTable("student_enrollments");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.PeriodId).IsRequired();
        builder.Property(x => x.GradeLevelId).IsRequired();
        builder.Property(x => x.GradeStrandCodedValueId);

        builder.Property(x => x.EnrolledOn).IsRequired();
        builder.Property(x => x.ExitDate);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasDefaultValue(EnrollmentStatus.Active);

        builder.Property(x => x.TransferReason)
            .HasColumnName("transfer_reason")
            .IsRequired(false);

        builder.HasIndex(x => new { x.TenantId, x.StudentId, x.PeriodId })
            .IsUnique()
            .HasDatabaseName("ix_student_enrollments_tenant_student_period");

        // NFR-3 hot paths (tenant_id leading).
        builder.HasIndex(x => new { x.TenantId, x.PeriodId })
            .HasDatabaseName("ix_student_enrollments_tenant_period");

        builder.HasIndex(x => new { x.TenantId, x.GradeLevelId })
            .HasDatabaseName("ix_student_enrollments_tenant_grade_level");

        builder.HasIndex(x => new { x.TenantId, x.GradeStrandCodedValueId })
            .HasDatabaseName("ix_student_enrollments_tenant_grade_strand");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("ix_student_enrollments_status");

        builder.Ignore(x => x.DomainEvents);
    }
}
