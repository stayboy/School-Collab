using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Teacher↔qualification link (grade-detail-rich-grids-plan.md §3). Coded
/// values live in the Settings database, so <see cref="TeacherQualification.CodedValueId"/>
/// is a bare tenant-scoped id (no FK), mirroring <see cref="TeacherGradeLevel.TeacherRoleCodedValueId"/>.
/// </summary>
internal sealed class TeacherQualificationConfiguration : TenantEntityTypeConfigurationBase<TeacherQualification>
{
    public TeacherQualificationConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TeacherQualification> builder)
    {
        builder.ToTable("teacher_qualifications");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.TeacherId).IsRequired();
        builder.Property(x => x.CodedValueId).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.TeacherId, x.CodedValueId })
            .IsUnique()
            .HasDatabaseName("ix_teacher_qualifications_tenant_teacher_qualification");
    }
}
