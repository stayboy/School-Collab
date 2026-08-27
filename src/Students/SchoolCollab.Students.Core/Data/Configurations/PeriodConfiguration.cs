using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). The "at most one current
/// period" / no-overlap invariant is per-tenant (§3.5/§5.6) — the "Tenant" filter
/// scopes <c>GetOverlappingPeriodsAsync</c> to the current tenant automatically.
/// </summary>
internal sealed class PeriodConfiguration : TenantEntityTypeConfigurationBase<Period>
{
    public PeriodConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<Period> builder)
    {
        builder.ToTable("periods");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.StartDate).IsRequired();
        builder.Property(x => x.EndDate).IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasDefaultValue(PeriodStatus.Draft);

        // Period hierarchy (FR-H1/H2). Additive: existing rows default to
        // AcademicYear with a null parent (back-filled by the migration).
        builder.Property(x => x.PeriodType)
            .IsRequired()
            .HasDefaultValue(PeriodType.AcademicYear);

        builder.Property(x => x.ParentPeriodId);

        // Self-referencing hierarchy FK: deleting an AcademicYear cascades its
        // sub-periods (EC-H1). A sub-period still cannot be hard-deleted while
        // activity-group memberships reference it (membership FK is RESTRICT).
        builder.HasOne<Period>()
            .WithMany()
            .HasForeignKey(x => x.ParentPeriodId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.NextPeriodId);

        // NFR-3 hot path: per-tenant current-period lookup and overlap check.
        builder.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("ix_periods_tenant_status");

        // NFR-H3 hot paths: active-year/active-sub-period lookups, sub-periods
        // of a year, and overlap checks.
        builder.HasIndex(x => new { x.TenantId, x.PeriodType, x.Status })
            .HasDatabaseName("ix_periods_tenant_type_status");

        builder.HasIndex(x => new { x.TenantId, x.ParentPeriodId, x.Status })
            .HasDatabaseName("ix_periods_tenant_parent_status");

        // H2.1 (FR-H4): at most one Active AcademicYear per tenant, and at most
        // one Active sub-period of each type per academic year. PeriodStatus.Active
        // = 1 (the spec §8.1 filter used 0, which is Draft — corrected here).
        builder.HasIndex(x => new { x.TenantId })
            .IsUnique()
            .HasFilter("period_type = 0 AND status = 1")
            .HasDatabaseName("ix_periods_one_active_year");

        builder.HasIndex(x => new { x.TenantId, x.ParentPeriodId, x.PeriodType })
            .IsUnique()
            .HasFilter("status = 1")
            .HasDatabaseName("ix_periods_one_active_sub_period");

        builder.HasIndex(x => x.StartDate)
            .HasDatabaseName("ix_periods_start_date");

        builder.Ignore(x => x.DomainEvents);
    }
}
