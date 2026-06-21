using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class GradeLevelConfiguration : EntityTypeConfigurationBase<GradeLevel>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GradeLevel> builder)
    {
        builder.ToTable("grade_levels");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.CodedValueId).IsRequired();

        builder.Property(x => x.Level).IsRequired();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.DisplayOrder).IsRequired();


        builder.HasIndex(x => x.CodedValueId)
            .HasDatabaseName("ix_grade_levels_coded_value_id");

        builder.HasIndex(x => x.Level)
            .HasDatabaseName("ix_grade_levels_level");

        builder.Ignore(x => x.DomainEvents);
    }
}