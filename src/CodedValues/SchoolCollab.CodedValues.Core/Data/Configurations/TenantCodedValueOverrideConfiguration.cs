using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.CodedValues.Core.Data.Configurations;

public sealed class TenantCodedValueOverrideConfiguration
    : TenantEntityTypeConfigurationBase<TenantCodedValueOverride>
{
    public TenantCodedValueOverrideConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TenantCodedValueOverride> builder)
    {
        builder.ToTable("tenant_coded_value_overrides");

        builder.Property(x => x.CodedValueId).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.CodedValueId }).IsUnique();

        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.Name).HasMaxLength(255);
    }
}
