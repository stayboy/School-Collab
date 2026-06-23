using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.CodedValues.Core.Data.Configurations;

public sealed class TenantCodedValueAttributeOverrideConfiguration
    : TenantEntityTypeConfigurationBase<TenantCodedValueAttributeOverride>
{
    public TenantCodedValueAttributeOverrideConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TenantCodedValueAttributeOverride> builder)
    {
        builder.ToTable("tenant_coded_value_attribute_overrides");

        builder.Property(x => x.GlobalCodedValueId).IsRequired();
        builder.Property(x => x.AttributeKey).IsRequired().HasMaxLength(100);
        builder.Property(x => x.CustomValue).IsRequired().HasMaxLength(1000);

        builder.HasIndex(x => new { x.TenantId, x.GlobalCodedValueId, x.AttributeKey }).IsUnique();
    }
}
