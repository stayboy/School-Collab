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

        builder.Property(x => x.NextPeriodId);

        // NFR-3 hot path: per-tenant current-period lookup and overlap check.
        builder.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("ix_periods_tenant_status");

        builder.HasIndex(x => x.StartDate)
            .HasDatabaseName("ix_periods_start_date");

        builder.Ignore(x => x.DomainEvents);
    }
}
