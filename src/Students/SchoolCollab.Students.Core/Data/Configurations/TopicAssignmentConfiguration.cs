using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). TPH root that maps the
/// shared <c>topic_assignments</c> table and the <c>topic_assignment_type</c>
/// discriminator selecting <see cref="GradeTopicAssignment"/> or
/// <see cref="ActivityGroupTopicAssignment"/>. The effective period is
/// date-based (<c>start_date</c> / <c>end_date</c>) by default, and may be
/// optionally scoped to a specific period via <c>period_id</c> (Rev. 6).
/// </summary>
internal sealed class TopicAssignmentConfiguration : EntityTypeConfigurationBase<TopicAssignment>
{
    private readonly Expression<Func<Guid>> _tenantIdAccessor;

    public TopicAssignmentConfiguration(Expression<Func<Guid>> tenantIdAccessor) =>
        _tenantIdAccessor = tenantIdAccessor;

    protected override void ConfigureEntity(EntityTypeBuilder<TopicAssignment> builder)
    {
        builder.ToTable("topic_assignments");

        builder.HasDiscriminator<string>("topic_assignment_type")
            .HasValue<GradeTopicAssignment>("grade")
            .HasValue<ActivityGroupTopicAssignment>("activity_group");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();
        builder.ConfigureTenantProperties();
        builder.ConfigureTenantQueryFilter(_tenantIdAccessor);

        builder.Property(x => x.TopicId).IsRequired();
        builder.Property(x => x.StartDate).IsRequired();
        builder.Property(x => x.EndDate);
        builder.Property(x => x.TopicStrandId);
        builder.Property(x => x.PeriodId).IsRequired(false);

        builder.HasOne<TopicStrand>()
            .WithMany()
            .HasForeignKey(x => x.TopicStrandId)
            .OnDelete(DeleteBehavior.SetNull);

        // Rev. 6 FR-55: optional period scope FK → periods.id (SetNull — removing
        // the period reverts the topic to year-spanning date-based delivery).
        builder.HasOne<Period>()
            .WithMany()
            .HasForeignKey(x => x.PeriodId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.TenantId, x.StartDate, x.EndDate })
            .HasDatabaseName("ix_topic_assignments_tenant_effective_dates");

        builder.Ignore(x => x.DomainEvents);
    }
}
