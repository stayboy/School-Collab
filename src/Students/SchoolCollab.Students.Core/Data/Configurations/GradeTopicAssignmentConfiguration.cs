using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// TPH subtype mapping for <see cref="GradeTopicAssignment"/>. Maps the
/// non-nullable <c>grade_level_id</c> FK and the per-subtype filtered unique
/// index that prevents duplicate grade→topic assignments (NULLs are distinct in
/// Postgres, so a composite index over a nullable column cannot enforce
/// uniqueness). The "Tenant" query filter is declared on the TPH root
/// (<see cref="TopicAssignmentConfiguration"/>) and inherited by this subtype.
/// </summary>
internal sealed class GradeTopicAssignmentConfiguration : IEntityTypeConfiguration<GradeTopicAssignment>
{
    public void Configure(EntityTypeBuilder<GradeTopicAssignment> builder)
    {
        builder.Property(x => x.GradeLevelId).IsRequired();

        builder.HasOne<GradeLevel>()
            .WithMany()
            .HasForeignKey(x => x.GradeLevelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.GradeLevelId, x.TopicId })
            .IsUnique()
            .HasFilter("\"topic_assignment_type\" = 'grade'")
            .HasDatabaseName("ix_topic_assignments_tenant_grade_topic_unique");
    }
}
