using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Teacher↔grade-level link (spec §4.12). v4: carries an optional subject
/// (<see cref="TeacherGradeLevel.TopicId"/>) so a row is a grade + optional
/// subject + role assignment. A teacher may hold multiple rows per grade (one
/// per subject); a partial unique index on grade-only rows (<c>topic_id IS NULL</c>)
/// prevents duplicate grade-only rows per teacher+grade.
/// </summary>
internal sealed class TeacherGradeLevelConfiguration : TenantEntityTypeConfigurationBase<TeacherGradeLevel>
{
    public TeacherGradeLevelConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TeacherGradeLevel> builder)
    {
        builder.ToTable("teacher_grade_levels");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.TeacherId).IsRequired();
        builder.Property(x => x.GradeLevelId).IsRequired();
        builder.Property(x => x.TopicId).IsRequired(false);
        builder.Property(x => x.TeacherRoleCodedValueId);

        // Non-unique: list all rows per teacher+grade (one per subject + optional grade-only row).
        builder.HasIndex(x => new { x.TenantId, x.TeacherId, x.GradeLevelId })
            .HasDatabaseName("ix_teacher_grade_levels_tenant_teacher_grade");

        // Partial unique: at most one grade-only row (topic_id IS NULL) per teacher+grade.
        builder.HasIndex(x => new { x.TenantId, x.TeacherId, x.GradeLevelId })
            .IsUnique()
            .HasFilter("\"topic_id\" IS NULL")
            .HasDatabaseName("ix_teacher_grade_levels_tenant_teacher_grade_unique_no_topic");

        // Supports the grade->teachers inverse query
        // (ListTeachersForGradeLevel, grade-level-detail-view-plan.md §3.1).
        builder.HasIndex(x => new { x.TenantId, x.GradeLevelId })
            .HasDatabaseName("ix_teacher_grade_levels_tenant_grade_level");
    }
}
