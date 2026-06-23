using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.CodedValues.Core.Domain;

namespace SchoolCollab.CodedValues.Core.Data.Configurations;

internal sealed class TenantCodedValueOverrideConfiguration : EntityTypeConfigurationBase<TenantCodedValueOverride>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TenantCodedValueOverride> builder)
    {
        builder.ToTable("tenant_coded_value_overrides");

        builder.ConfigureAuditProperties();
        
        builder.Property(x => x.TenantId).IsRequired();
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