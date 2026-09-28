using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant-scoped subject enrollment exception (subject-period-exception-model.md
/// v3 §2/§7). Soft-deleted (so the reason a subject was unavailable survives) and
/// carrying the PostgreSQL <c>xmin</c> row version.
///
/// <para><b>No period relation.</b> The v1 <c>period_id</c> column, its
/// <c>ON DELETE RESTRICT</c> foreign key and its index are all gone: an exception is a
/// period <i>part</i> plus a date span, never a period instance (§0 decision 7), so
/// there is nothing to orphan or retire.</para>
///
/// <para><b>The unique key is not in this model.</b> Both bounds are nullable and
/// Postgres treats NULLs as distinct, so uniqueness has to be a COALESCE expression
/// index — created by the migration in raw SQL (§7) and deliberately absent here, so
/// <c>MigrationGuardTests.NoUncommittedModelChanges</c> stays green (a SQL-only index
/// is invisible to the model). What this configuration declares is only the plain
/// <b>non-unique</b> lookup indexes.</para>
///
/// <para>Every index and foreign key carries an <b>explicit</b> name: an
/// auto-generated name for the activity-group FK overflows Postgres's 63-byte
/// identifier cap and is silently truncated, which desynchronised the model snapshot
/// in pass 1 of this round.</para>
/// </summary>
internal sealed class SubjectEnrollmentExceptionConfiguration
    : TenantEntityTypeConfigurationBase<SubjectEnrollmentException>
{
    public SubjectEnrollmentExceptionConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<SubjectEnrollmentException> builder)
    {
        builder.ToTable("subject_enrollment_exceptions");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.GradeLevelId);
        builder.Property(x => x.ActivityGroupId);
        builder.Property(x => x.TopicId).IsRequired();
        // int-backed enum, defaulting to None — mirrors PeriodConfiguration's
        // x.Division (the type is descriptive only; availability is a date test).
        builder.Property(x => x.Division)
            .IsRequired()
            .HasDefaultValue(AcademicYearDivision.None);
        builder.Property(x => x.StartDate);
        builder.Property(x => x.EndDate);
        builder.Property(x => x.Reason);

        // An exception dies with its grade / activity group / topic.
        builder.HasOne<GradeLevel>()
            .WithMany()
            .HasForeignKey(x => x.GradeLevelId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_subject_enrollment_exceptions_grade_levels_grade_level_id");

        builder.HasOne<ActivityGroup>()
            .WithMany()
            .HasForeignKey(x => x.ActivityGroupId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_subject_enrollment_exceptions_activity_groups_activity_grou");

        builder.HasOne<Topic>()
            .WithMany()
            .HasForeignKey(x => x.TopicId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_subject_enrollment_exceptions_subjects_topic_id");

        builder.HasIndex(x => new { x.TenantId, x.GradeLevelId, x.TopicId, x.Division })
            .HasDatabaseName("ix_subject_enrollment_exceptions_tenant_grade_topic_division");

        builder.HasIndex(x => new { x.TenantId, x.ActivityGroupId, x.TopicId, x.Division })
            .HasDatabaseName("ix_subject_enrollment_exceptions_tenant_group_topic_division");
    }
}
