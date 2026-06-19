using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.CodedValues.Core.Domain;

namespace SchoolCollab.CodedValues.Core.Data.Configurations;

public sealed class TenantCodedValueAttributeOverrideConfiguration : IEntityTypeConfiguration<TenantCodedValueAttributeOverride>
{
    public void Configure(EntityTypeBuilder<TenantCodedValueAttributeOverride> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.CodedValueId).IsRequired();
        builder.Property(x => x.AttributeKey).IsRequired().HasMaxLength(100);
        builder.Property(x => x.CustomValue).IsRequired().HasMaxLength(1000);
        
        builder.HasIndex(x => new { x.TenantId, x.CodedValueId, x.AttributeKey }).IsUnique();
    }
}
