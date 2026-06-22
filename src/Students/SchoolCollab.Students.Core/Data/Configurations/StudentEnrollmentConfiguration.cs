using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class StudentEnrollmentConfiguration : EntityTypeConfigurationBase<StudentEnrollment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<StudentEnrollment> builder)
    {
        builder.ToTable("student_enrollments");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.PeriodId).IsRequired();
        builder.Property(x => x.GradeLevelId).IsRequired();

        builder.Property(x => x.EnrolledOn).IsRequired();
        builder.Property(x => x.ExitDate);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasDefaultValue(EnrollmentStatus.Active);


        builder.HasIndex(x => new { x.StudentId, x.PeriodId })
            .HasDatabaseName("ix_student_enrollments_student_period");

        builder.HasIndex(x => x.PeriodId)
            .HasDatabaseName("ix_student_enrollments_period");

        builder.HasIndex(x => x.GradeLevelId)
            .HasDatabaseName("ix_student_enrollments_grade_level");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("ix_student_enrollments_status");

        builder.Ignore(x => x.DomainEvents);
    }
}