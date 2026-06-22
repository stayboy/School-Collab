using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.CodedValues.Core.Domain;

namespace SchoolCollab.CodedValues.Core.Data.Configurations;

public sealed class TenantCodedValueOverrideConfiguration : EntityTypeConfigurationBase<TenantCodedValueOverride>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TenantCodedValueOverride> builder)
    {
        builder.Property(x => x.CodedValueId).IsRequired();
        
        builder.HasIndex(x => new { x.TenantId, x.CodedValueId }).IsUnique();
        
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.Name).HasMaxLength(255);
    }
}
