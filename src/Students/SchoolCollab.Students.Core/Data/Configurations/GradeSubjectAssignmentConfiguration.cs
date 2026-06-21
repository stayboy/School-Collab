using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class GradeSubjectAssignmentConfiguration : EntityTypeConfigurationBase<GradeSubjectAssignment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GradeSubjectAssignment> builder)
    {
        builder.ToTable("grade_subject_assignments");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.GradeLevelId).IsRequired();
        builder.Property(x => x.SubjectId).IsRequired();
        builder.Property(x => x.PeriodId).IsRequired();


        builder.HasIndex(x => new { x.GradeLevelId, x.SubjectId, x.PeriodId })
            .IsUnique()
            .HasDatabaseName("ix_grade_subject_assignments_unique");

        builder.HasIndex(x => x.PeriodId)
            .HasDatabaseName("ix_grade_subject_assignments_period");

        builder.Ignore(x => x.DomainEvents);
    }
}