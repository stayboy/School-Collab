using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

/// <summary>
/// Configures <see cref="TenantEntityCodeRuleOverride"/> as a <b>strict</b>
/// tenant entity (global-tenant-filter.md §3.2): non-null <c>tenant_id</c>
/// with the named "Tenant" filter <c>TenantId == CurrentTenantId</c>, so
/// each tenant's override rows are isolated. The default-tenant
/// (<c>Guid.Empty</c>) override path is permitted by the
/// <c>ITenantContextAccessor</c>-bypass in the upsert/remove handlers
/// (mirrors the <see cref="TenantCodedValueOverrideConfiguration"/> pattern).
/// </summary>
internal sealed class TenantEntityCodeRuleOverrideConfiguration
    : TenantEntityTypeConfigurationBase<TenantEntityCodeRuleOverride>
{
    public TenantEntityCodeRuleOverrideConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TenantEntityCodeRuleOverride> builder)
    {
        builder.ToTable("tenant_entity_code_rule_overrides");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.GenerationRuleId).IsRequired();
        builder.Property(x => x.EntityCodeSegmentId).IsRequired();
        builder.Property(x => x.Field).IsRequired().HasConversion<int>();
        builder.Property(x => x.Value).IsRequired().HasMaxLength(200);

        // Only one override per (tenant, rule, segment, field). Replacing the
        // value of an existing field is an UPDATE, not a new row.
        builder.HasIndex(x => new { x.TenantId, x.GenerationRuleId, x.EntityCodeSegmentId, x.Field })
            .IsUnique()
            .HasDatabaseName("ix_tenant_entity_code_rule_overrides_unique");

        builder.HasIndex(x => new { x.TenantId, x.GenerationRuleId })
            .HasDatabaseName("ix_tenant_entity_code_rule_overrides_rule");
    }
}
