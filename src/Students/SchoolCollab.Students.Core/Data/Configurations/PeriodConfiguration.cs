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

        // Period hierarchy (plan-drop-periodtype.md). The single kind field is
        // AcademicYearDivision (None/Terms/Semesters), non-nullable with a None
        // default. ParentPeriodId is null for a top-level academic year and set
        // for a Term/Semester sub-period.
        builder.Property(x => x.Division)
            .IsRequired()
            .HasDefaultValue(AcademicYearDivision.None);

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
        builder.HasIndex(x => new { x.TenantId, x.Division, x.Status })
            .HasDatabaseName("ix_periods_tenant_division_status");

        builder.HasIndex(x => new { x.TenantId, x.ParentPeriodId, x.Status })
            .HasDatabaseName("ix_periods_tenant_parent_status");

        // H2.1 (FR-H4): at most one Active top-level academic year per tenant, and
        // at most one Active sub-period per parent academic year. PeriodStatus.Active
        // = 1 (the spec §8.1 filter used 0, which is Draft — corrected here).
        builder.HasIndex(x => new { x.TenantId })
            .IsUnique()
            .HasFilter("parent_period_id IS NULL AND status = 1")
            .HasDatabaseName("ix_periods_one_active_year");

        builder.HasIndex(x => new { x.TenantId, x.ParentPeriodId })
            .IsUnique()
            .HasFilter("parent_period_id IS NOT NULL AND status = 1")
            .HasDatabaseName("ix_periods_one_active_sub_period");

        builder.HasIndex(x => x.StartDate)
            .HasDatabaseName("ix_periods_start_date");

        builder.Ignore(x => x.DomainEvents);
    }
}
