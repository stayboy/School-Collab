using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class SubjectStrandConfiguration : EntityTypeConfigurationBase<SubjectStrand>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SubjectStrand> builder)
    {
        builder.ToTable("subject_strands");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.DisplayOrder).IsRequired();
        builder.Property(x => x.SubjectId).IsRequired();

        builder.HasOne(x => x.Subject)
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.SubjectId)
            .HasDatabaseName("ix_subject_strands_subject");

        builder.Ignore(x => x.DomainEvents);
    }
}