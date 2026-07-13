using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Teacher↔grade-level link (spec §4.12).
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

        builder.HasIndex(x => new { x.TenantId, x.TeacherId, x.GradeLevelId })
            .IsUnique()
            .HasDatabaseName("ix_teacher_grade_levels_tenant_teacher_grade_level");
    }
}
