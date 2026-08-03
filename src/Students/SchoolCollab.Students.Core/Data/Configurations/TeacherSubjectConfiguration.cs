using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Teacher↔subject link (spec §4.12).
/// </summary>
internal sealed class TeacherSubjectConfiguration : TenantEntityTypeConfigurationBase<TeacherSubject>
{
    public TeacherSubjectConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TeacherSubject> builder)
    {
        builder.ToTable("teacher_subjects");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.TeacherId).IsRequired();
        builder.Property(x => x.TopicId).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.TeacherId, x.TopicId })
            .IsUnique()
            .HasDatabaseName("ix_teacher_subjects_tenant_teacher_subject");
    }
}
