using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class SubjectLessonConfiguration : EntityTypeConfigurationBase<SubjectLesson>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SubjectLesson> builder)
    {
        builder.ToTable("subject_lessons");

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

        builder.HasOne(x => x.Strand)
            .WithMany()
            .HasForeignKey(x => x.StrandId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.SubjectId)
            .HasDatabaseName("ix_subject_lessons_subject");

        builder.HasIndex(x => x.StrandId)
            .HasDatabaseName("ix_subject_lessons_strand");

        builder.Ignore(x => x.DomainEvents);
    }
}