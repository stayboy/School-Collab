using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class StudentSubjectAssignmentConfiguration : EntityTypeConfigurationBase<StudentSubjectAssignment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<StudentSubjectAssignment> builder)
    {
        builder.ToTable("student_subject_assignments");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.SubjectId).IsRequired();
        builder.Property(x => x.PeriodId).IsRequired();

        builder.Property(x => x.IsOverride)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.SourceType)
            .IsRequired()
            .HasDefaultValue(SubjectAssignmentSource.GradeAssignment);


        builder.HasIndex(x => new { x.StudentId, x.SubjectId, x.PeriodId })
            .IsUnique()
            .HasDatabaseName("ix_student_subject_assignments_unique");

        builder.HasIndex(x => x.PeriodId)
            .HasDatabaseName("ix_student_subject_assignments_period");

        builder.Ignore(x => x.DomainEvents);
    }
}