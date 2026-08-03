using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// TPH subtype mapping for <see cref="ActivityGroupTopicAssignment"/>. Maps the
/// non-nullable <c>activity_group_id</c> FK and the per-subtype filtered unique
/// index that prevents duplicate group→topic assignments (NULLs are distinct in
/// Postgres, so a composite index over a nullable column cannot enforce
/// uniqueness). The "Tenant" query filter is declared on the TPH root
/// (<see cref="TopicAssignmentConfiguration"/>) and inherited by this subtype.
/// </summary>
internal sealed class ActivityGroupTopicAssignmentConfiguration : IEntityTypeConfiguration<ActivityGroupTopicAssignment>
{
    public void Configure(EntityTypeBuilder<ActivityGroupTopicAssignment> builder)
    {
        builder.Property(x => x.ActivityGroupId).IsRequired();

        builder.HasOne<ActivityGroup>()
            .WithMany()
            .HasForeignKey(x => x.ActivityGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.ActivityGroupId, x.TopicId })
            .IsUnique()
            .HasFilter("\"topic_assignment_type\" = 'activity_group'")
            .HasDatabaseName("ix_topic_assignments_tenant_group_topic_unique");
    }
}
