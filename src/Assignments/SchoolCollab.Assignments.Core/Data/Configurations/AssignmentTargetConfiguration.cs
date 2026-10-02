using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (documents/specs/assignment-authoring-compartments.md §7.1 TGT-1).
/// The <c>assignment_id</c> FK is CASCADE (deleting an assignment removes its targets);
/// <c>ref_id</c> is an operational ref (no cross-context FK, the
/// <c>AssignmentActivityGroup</c> precedent). <c>kind</c> is stored as its name so the
/// backfill SQL and the partial indexes can filter on the literal <c>'AllStudents'</c>.
/// </summary>
internal sealed class AssignmentTargetConfiguration : TenantEntityTypeConfigurationBase<AssignmentTarget>
{
    public AssignmentTargetConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<AssignmentTarget> builder)
    {
        builder.ToTable("assignment_targets");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.AssignmentId).IsRequired();
        builder.Property(x => x.Kind).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.RefId);
        builder.Property(x => x.DisplayOrder).IsRequired();

        builder.HasOne<Assignment>()
            .WithMany(a => a.Targets)
            .HasForeignKey(x => x.AssignmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // TGT-1: no duplicate (kind, ref) constraint per assignment — the uniqueness the
        // aggregate enforces in SetTargets, backstopped at the DB level. The two indexes are
        // declared with explicit names because EF identifies an index by its property set
        // unless a name is given, and the NULL-\`ref_id\` AllStudents row needs its own
        // partial index (Postgres treats NULLs as distinct in a unique index).
        builder.HasIndex(x => new { x.AssignmentId, x.Kind, x.RefId },
                "uq_assignment_targets_assignment_kind_ref")
            .IsUnique()
            .HasDatabaseName("uq_assignment_targets_assignment_kind_ref");

        builder.HasIndex(x => new { x.AssignmentId, x.Kind, x.RefId },
                "ux_assignment_targets_all_students")
            .IsUnique()
            .HasDatabaseName("ux_assignment_targets_all_students")
            .HasFilter("\"kind\" = 'AllStudents' AND \"ref_id\" IS NULL");

        // The FR-6 delete-guard read (a group referenced by a target blocks the hard delete).
        builder.HasIndex(x => new { x.TenantId, x.Kind, x.RefId })
            .HasDatabaseName("ix_assignment_targets_tenant_kind_ref");
    }
}
