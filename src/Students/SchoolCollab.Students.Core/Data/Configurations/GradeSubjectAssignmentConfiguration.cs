using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class GradeSubjectAssignmentConfiguration : IEntityTypeConfiguration<GradeSubjectAssignment>
{
    public void Configure(EntityTypeBuilder<GradeSubjectAssignment> builder)
    {
        builder.ToTable("grade_subject_assignments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.GradeLevelId).IsRequired();
        builder.Property(x => x.SubjectId).IsRequired();
        builder.Property(x => x.PeriodId).IsRequired();

        builder.Property(x => x.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => new { x.GradeLevelId, x.SubjectId, x.PeriodId })
            .IsUnique()
            .HasDatabaseName("ix_grade_subject_assignments_unique");

        builder.HasIndex(x => x.PeriodId)
            .HasDatabaseName("ix_grade_subject_assignments_period");

        builder.Ignore(x => x.DomainEvents);
    }
}