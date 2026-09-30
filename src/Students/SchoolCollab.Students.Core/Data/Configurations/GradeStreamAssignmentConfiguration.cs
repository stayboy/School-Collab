using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Standalone (non-TPH) mapping for the grade↔stream bridge. Strict tenant entity
/// (global-tenant-filter.md §3.2): the base class applies the tenant column and
/// the named "Tenant" query filter from the <c>CurrentTenantId</c> accessor.
/// </summary>
/// <remarks>
/// <see cref="GradeStreamAssignment.StreamCodedValueId"/> is intentionally
/// <b>not</b> mapped as a relationship: coded values live in the Settings
/// database, so no FK can exist. Uniqueness is enforced by
/// <c>ix_grade_stream_assignments_tenant_grade_stream</c> instead — the index is
/// also what makes the assign flow's insert idempotent.
/// </remarks>
internal sealed class GradeStreamAssignmentConfiguration : TenantEntityTypeConfigurationBase<GradeStreamAssignment>
{
    public GradeStreamAssignmentConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<GradeStreamAssignment> builder)
    {
        builder.ToTable("grade_stream_assignments");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.GradeLevelId).IsRequired();
        builder.Property(x => x.StreamCodedValueId).IsRequired();

        // Ordering authority for the grade's stream list (the coded value's own
        // DisplayOrder orders the cross-grade GRSTREAMS catalogue instead).
        builder.Property(x => x.DisplayOrder).IsRequired().HasDefaultValue(0);

        builder.HasOne<GradeLevel>()
            .WithMany()
            .HasForeignKey(x => x.GradeLevelId)
            .OnDelete(DeleteBehavior.Cascade);

        // A stream is offered by a grade at most once. Also the idempotency backstop
        // for the assign flow's race window (AddOrReuseAsync).
        builder.HasIndex(x => new { x.TenantId, x.GradeLevelId, x.StreamCodedValueId })
            .IsUnique()
            .HasDatabaseName("ix_grade_stream_assignments_tenant_grade_stream");
    }
}
