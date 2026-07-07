using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class SubjectConfiguration : EntityTypeConfigurationBase<Subject>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("subjects");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.CodedValueId).IsRequired();

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.DisplayOrder).IsRequired();


        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("ix_subjects_code");

        builder.HasIndex(x => x.CodedValueId)
            .IsUnique()
            .HasDatabaseName("ix_subjects_coded_value_id");

        builder.Ignore(x => x.DomainEvents);
    }
}