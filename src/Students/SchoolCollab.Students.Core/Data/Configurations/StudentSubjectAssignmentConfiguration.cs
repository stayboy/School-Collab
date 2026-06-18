using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class StudentSubjectAssignmentConfiguration : IEntityTypeConfiguration<StudentSubjectAssignment>
{
    public void Configure(EntityTypeBuilder<StudentSubjectAssignment> builder)
    {
        builder.ToTable("student_subject_assignments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.SubjectId).IsRequired();
        builder.Property(x => x.PeriodId).IsRequired();

        builder.Property(x => x.IsOverride)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.SourceType)
            .IsRequired()
            .HasDefaultValue(SubjectAssignmentSource.GradeAssignment);

        builder.Property(x => x.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => new { x.StudentId, x.SubjectId, x.PeriodId })
            .IsUnique()
            .HasDatabaseName("ix_student_subject_assignments_unique");

        builder.HasIndex(x => x.PeriodId)
            .HasDatabaseName("ix_student_subject_assignments_period");

        builder.Ignore(x => x.DomainEvents);
    }
}