using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Teacher↔topic link (spec §4.12). Subject->Topic rename (FR-13).
/// </summary>
internal sealed class TeacherTopicConfiguration : TenantEntityTypeConfigurationBase<TeacherTopic>
{
    public TeacherTopicConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TeacherTopic> builder)
    {
        builder.ToTable("teacher_topics");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.TeacherId).IsRequired();
        builder.Property(x => x.TopicId).IsRequired();
        builder.Property(x => x.RoleCodedValueId);

        builder.Property(x => x.StartDate)
            .HasColumnName("start_date")
            .IsRequired();
        builder.Property(x => x.EndDate)
            .HasColumnName("end_date")
            .IsRequired(false);

        builder.HasIndex(x => new { x.TenantId, x.TeacherId, x.TopicId })
            .IsUnique()
            .HasDatabaseName("ix_teacher_topics_tenant_teacher_topic");
    }
}
