using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

internal sealed class TenantFeatureFlagOverrideConfiguration
    : TenantEntityTypeConfigurationBase<TenantFeatureFlagOverride>
{
    public TenantFeatureFlagOverrideConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TenantFeatureFlagOverride> builder)
    {
        builder.ToTable("tenant_flag_overrides");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.FeatureFlagId).IsRequired();

        builder.Property(x => x.IsEnabled);

        builder.Property(x => x.Reason)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.EffectiveFrom);
        builder.Property(x => x.EffectiveTo);

        builder.HasOne<FeatureFlag>()
            .WithMany()
            .HasForeignKey(x => x.FeatureFlagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.FeatureFlagId })
            .IsUnique()
            .HasDatabaseName("ix_tenant_flag_overrides_unique")
            .HasFilter("is_deleted = false");

        builder.HasIndex(x => x.FeatureFlagId)
            .HasDatabaseName("ix_tenant_flag_overrides_flag");
    }
}