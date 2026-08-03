using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). Inherits the subject's
/// tenant.
/// </summary>
internal sealed class TopicStrandConfiguration : TenantEntityTypeConfigurationBase<TopicStrand>
{
    public TopicStrandConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TopicStrand> builder)
    {
        builder.ToTable("subject_strands");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.DisplayOrder).IsRequired();
        builder.Property(x => x.TopicId).IsRequired();

        builder.HasOne(x => x.Topic)
            .WithMany()
            .HasForeignKey(x => x.TopicId)
            .OnDelete(DeleteBehavior.Cascade);

        // NFR-3 hot path (tenant_id leading).
        builder.HasIndex(x => new { x.TenantId, x.TopicId })
            .HasDatabaseName("ix_subject_strands_tenant_subject");

        builder.Ignore(x => x.DomainEvents);
    }
}
