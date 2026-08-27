using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). Inherits the student's/
/// group's tenant. A partial unique index on (tenant_id, student_id,
/// activity_group_id) filtered to status = 0 (Active) enforces at most one
/// active membership per (tenant, student, group) — FR-10. The
/// activity_group_id FK is ON DELETE RESTRICT (NFR-8/FR-6 — a group with any
/// membership row cannot be hard-deleted, only Archived).
/// </summary>
internal sealed class ActivityGroupMembershipConfiguration : TenantEntityTypeConfigurationBase<ActivityGroupMembership>
{
    public ActivityGroupMembershipConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<ActivityGroupMembership> builder)
    {
        builder.ToTable("activity_group_memberships");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.ActivityGroupId).IsRequired();
        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.PeriodId).IsRequired(false);
        builder.Property(x => x.AutoRenew)
            .IsRequired()
            .HasDefaultValue(true);
        builder.Property(x => x.WindowStartDate).IsRequired(false);
        builder.Property(x => x.WindowEndDate).IsRequired(false);
        builder.Property(x => x.JoinedOn).IsRequired();
        builder.Property(x => x.ExitedOn).IsRequired(false);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasDefaultValue(MembershipStatus.Active);

        // FR-6 / NFR-8: ON DELETE RESTRICT — a group with any membership row
        // (any status) cannot be hard-deleted, preserving membership history.
        builder.HasOne<ActivityGroup>()
            .WithMany()
            .HasForeignKey(x => x.ActivityGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        // student_id FK → students.id. Students are soft-deleted (never
        // hard-deleted), so Restrict preserves membership history (EC-2).
        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Rev. 2 FR-7/10: optional period scope FK → periods.id (SetNull when the
        // period is removed — the membership loses its period scope, not the student).
        builder.HasOne<Period>()
            .WithMany()
            .HasForeignKey(x => x.PeriodId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // FR-10 (Rev. 2 §8.2): at most one ACTIVE membership per
        // (tenant, student, group). Split into two partial unique indexes to
        // dodge the Postgres NULL-distinct pitfall: period-scoped memberships
        // (period_id NOT NULL) are unique per (tenant, student, group, period);
        // window-scoped (period_id NULL) are unique per (tenant, student, group).
        // Re-joining after exit creates a new Active row without violating the
        // constraint (the old row is Exited/Removed).
        builder.HasIndex(x => new { x.TenantId, x.StudentId, x.ActivityGroupId, x.PeriodId })
            .IsUnique()
            .HasFilter("status = 0 AND period_id IS NOT NULL")
            .HasDatabaseName("ix_agm_tenant_student_group_period_active");

        builder.HasIndex(x => new { x.TenantId, x.StudentId, x.ActivityGroupId })
            .IsUnique()
            .HasFilter("status = 0 AND period_id IS NULL")
            .HasDatabaseName("ix_agm_tenant_student_group_active");

        // NFR-3 hot paths (tenant_id leading).
        builder.HasIndex(x => new { x.TenantId, x.ActivityGroupId, x.Status })
            .HasDatabaseName("ix_agm_tenant_group_status");

        builder.HasIndex(x => new { x.TenantId, x.StudentId })
            .HasDatabaseName("ix_agm_tenant_student");

        builder.Ignore(x => x.DomainEvents);
    }
}
