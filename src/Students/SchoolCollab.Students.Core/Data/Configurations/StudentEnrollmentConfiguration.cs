using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class StudentEnrollmentConfiguration : IEntityTypeConfiguration<StudentEnrollment>
{
    public void Configure(EntityTypeBuilder<StudentEnrollment> builder)
    {
        builder.ToTable("student_enrollments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.PeriodId).IsRequired();
        builder.Property(x => x.GradeLevelId).IsRequired();

        builder.Property(x => x.EnrolledOn).IsRequired();
        builder.Property(x => x.ExitDate);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasDefaultValue(EnrollmentStatus.Active);

        builder.Property(x => x.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

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