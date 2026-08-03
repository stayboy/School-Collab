using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). The assignment_id FK is
/// CASCADE (deleting an assignment removes its group links); activity_group_id is
/// an operational ref (no cross-context FK). Unique (tenant, assignment, group)
/// prevents duplicate links (spec §8.3).
/// </summary>
internal sealed class AssignmentActivityGroupConfiguration : TenantEntityTypeConfigurationBase<AssignmentActivityGroup>
{
    public AssignmentActivityGroupConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<AssignmentActivityGroup> builder)
    {
        builder.ToTable("assignment_activity_groups");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.AssignmentId).IsRequired();
        builder.Property(x => x.ActivityGroupId).IsRequired();

        builder.HasOne<Assignment>()
            .WithMany()
            .HasForeignKey(x => x.AssignmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // FR-17 / §8.3: unique (tenant, assignment, group) → no duplicate links.
        builder.HasIndex(x => new { x.TenantId, x.AssignmentId, x.ActivityGroupId })
            .IsUnique()
            .HasDatabaseName("uq_assignment_activity_groups_tenant_assignment_group");

        // NFR-3 reverse lookup (tenant_id leading) for the FR-6 delete guard.
        builder.HasIndex(x => new { x.TenantId, x.ActivityGroupId })
            .HasDatabaseName("ix_assignment_activity_groups_tenant_group");
    }
}
