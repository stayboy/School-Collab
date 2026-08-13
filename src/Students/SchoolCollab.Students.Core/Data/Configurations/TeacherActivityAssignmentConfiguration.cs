using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Teacher↔activity-group assignment (v4 spec §3.5). Grades carried by the
/// <see cref="TeacherActivityAssignmentGrade"/> join. Strict tenant entity.
/// </summary>
internal sealed class TeacherActivityAssignmentConfiguration : TenantEntityTypeConfigurationBase<TeacherActivityAssignment>
{
    public TeacherActivityAssignmentConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TeacherActivityAssignment> builder)
    {
        builder.ToTable("teacher_activity_assignments");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.TeacherId).IsRequired();
        builder.Property(x => x.ActivityGroupId).IsRequired();
        builder.Property(x => x.RoleCodedValueId).IsRequired(false);

        builder.HasOne<ActivityGroup>()
            .WithMany()
            .HasForeignKey(x => x.ActivityGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Grades)
            .WithOne()
            .HasForeignKey(x => x.TeacherActivityAssignmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.TeacherId })
            .HasDatabaseName("ix_teacher_activity_assignments_tenant_teacher");

        builder.HasIndex(x => new { x.TenantId, x.ActivityGroupId })
            .HasDatabaseName("ix_teacher_activity_assignments_tenant_activity");
    }
}

/// <summary>
/// Join row linking a <see cref="TeacherActivityAssignment"/> to a grade.
/// Strict tenant entity.
/// </summary>
internal sealed class TeacherActivityAssignmentGradeConfiguration : TenantEntityTypeConfigurationBase<TeacherActivityAssignmentGrade>
{
    public TeacherActivityAssignmentGradeConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TeacherActivityAssignmentGrade> builder)
    {
        builder.ToTable("teacher_activity_assignment_grades");

        builder.Property(x => x.TeacherActivityAssignmentId).IsRequired();
        builder.Property(x => x.GradeLevelId).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.TeacherActivityAssignmentId })
            .HasDatabaseName("ix_teac_act_grade_tenant_assignment");

        builder.HasIndex(x => new { x.TenantId, x.GradeLevelId })
            .HasDatabaseName("ix_teac_act_grade_tenant_grade");
    }
}
