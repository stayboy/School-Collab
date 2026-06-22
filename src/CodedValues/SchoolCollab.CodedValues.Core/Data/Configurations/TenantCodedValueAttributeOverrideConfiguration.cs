using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.CodedValues.Core.Domain;

namespace SchoolCollab.CodedValues.Core.Data.Configurations;

public sealed class TenantCodedValueAttributeOverrideConfiguration : EntityTypeConfigurationBase<TenantCodedValueAttributeOverride>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TenantCodedValueAttributeOverride> builder)
    {
        builder.ConfigureTenantProperties();

        builder.Property(x => x.CodedValueId).IsRequired();
        builder.Property(x => x.AttributeKey).IsRequired().HasMaxLength(100);
        builder.Property(x => x.CustomValue).IsRequired().HasMaxLength(1000);
        
        builder.HasIndex(x => new { x.TenantId, x.CodedValueId, x.AttributeKey }).IsUnique();
    }
}
