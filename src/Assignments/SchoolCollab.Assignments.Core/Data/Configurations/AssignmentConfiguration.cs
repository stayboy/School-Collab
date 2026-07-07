using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

internal sealed class AssignmentConfiguration : TenantEntityTypeConfigurationBase<Assignment>
{
    public AssignmentConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<Assignment> builder)
    {
        builder.ToTable("assignments");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasMaxLength(5000);

        builder.Property(x => x.AssignmentType)
            .IsRequired()
            .HasDefaultValue(AssignmentType.Digital);

        builder.Property(x => x.GradingFormat)
            .IsRequired()
            .HasDefaultValue(GradingFormat.TeacherGraded);

        builder.Property(x => x.TargetAudienceType)
            .IsRequired()
            .HasDefaultValue(TargetAudienceType.AllStudents);

        builder.Property(x => x.SubjectId);
        builder.Property(x => x.GradeLevelId);

        builder.Property(x => x.DueDate);

        builder.Property(x => x.MaxScore)
            .HasPrecision(5, 2);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasDefaultValue(AssignmentStatus.Draft);

        builder.Property(x => x.CreatedByTeacherId)
            .IsRequired();


        builder.HasIndex(x => x.SubjectId)
            .HasDatabaseName("ix_assignments_subject_id");

        builder.HasIndex(x => x.GradeLevelId)
            .HasDatabaseName("ix_assignments_grade_level_id");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("ix_assignments_status");

        builder.HasIndex(x => x.CreatedByTeacherId)
            .HasDatabaseName("ix_assignments_teacher_id");

        builder.Ignore(x => x.DomainEvents);

        builder.Navigation(x => x.Questions).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.Navigation(x => x.Reviews).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.Navigation(x => x.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.OwnsMany(x => x.Attachments, a =>
        {
            a.ToTable("assignment_attachments");
            a.WithOwner().HasForeignKey(a => a.AssignmentId);
            a.HasKey(a => a.Id);
            a.Property(a => a.FileName).IsRequired().HasMaxLength(255);
            a.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
            a.Property(a => a.FileSize).IsRequired();
            a.Property(a => a.StoragePath).IsRequired().HasMaxLength(500);
        });

        builder.OwnsMany(x => x.Questions, q =>
        {
            q.ToTable("assignment_questions");
            q.WithOwner().HasForeignKey(q => q.AssignmentId);
            q.HasKey(q => q.Id);
            q.Property(q => q.QuestionText).IsRequired().HasMaxLength(2000);
            q.Property(q => q.QuestionType).IsRequired().HasDefaultValue(QuestionType.MultipleChoice);
            q.Property(q => q.DisplayOrder).IsRequired();
            q.Property(q => q.CorrectOptionId);

            q.OwnsMany(q => q.Options, o =>
            {
                o.ToTable("question_options");
                o.WithOwner().HasForeignKey(o => o.QuestionId);
                o.HasKey(o => o.Id);
                o.Property(o => o.OptionText).IsRequired().HasMaxLength(500);
                o.Property(o => o.IsCorrect).IsRequired().HasDefaultValue(false);
            });
        });

        builder.OwnsMany(x => x.Reviews, r =>
        {
            r.ToTable("assignment_reviews");
            r.WithOwner().HasForeignKey(r => r.AssignmentId);
            r.HasKey(r => r.Id);
            r.Property(r => r.TeacherId).IsRequired();
            r.Property(r => r.Score).HasPrecision(5, 2);
            r.Property(r => r.Comments).HasMaxLength(2000);
            r.Property(r => r.ReviewDate).IsRequired();
        });
    }
}