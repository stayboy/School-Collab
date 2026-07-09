using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

/// <summary>
/// Configures <see cref="TenantCodedValueOverride"/> as a <b>strict</b> tenant entity
/// (global-tenant-filter.md §3.2): non-null <c>tenant_id</c> with the named "Tenant"
/// filter <c>TenantId == CurrentTenantId</c>, so each tenant's override rows (including
/// the default/dev sentinel's <c>Guid.Empty</c> row) are isolated by the query filter.
/// Targets shared-blueprint (<c>NULL</c>) <see cref="CodedValue"/> rows only — the
/// override pattern is retained (AC-8). The default-tenant (<c>Guid.Empty</c>) override
/// save path suppresses the save-guard via <c>ITenantContextAccessor</c> in the
/// upsert/remove handlers (FR-10 sanctioned bypass for the dev affordance).
/// </summary>
internal sealed class TenantCodedValueOverrideConfiguration
    : TenantEntityTypeConfigurationBase<TenantCodedValueOverride>
{
    public TenantCodedValueOverrideConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TenantCodedValueOverride> builder)
    {
        builder.ToTable("tenant_coded_value_overrides");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.GlobalCodedValueId).IsRequired();
        builder.Property(x => x.OverriddenName).HasMaxLength(200);
        builder.Property(x => x.OverriddenDescription).HasMaxLength(1000);

        builder.HasIndex(x => new { x.TenantId, x.GlobalCodedValueId })
            .IsUnique()
            .HasDatabaseName("ix_tenant_coded_value_overrides_unique");

        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_tenant_coded_value_overrides_tenant");
    }
}
