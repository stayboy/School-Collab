using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.CodedValues.Core.Domain;

namespace SchoolCollab.CodedValues.Core.Data.Configurations;

public sealed class TenantCodedValueOverrideConfiguration : IEntityTypeConfiguration<TenantCodedValueOverride>
{
    public void Configure(EntityTypeBuilder<TenantCodedValueOverride> builder)
    {
        builder.HasKey(x => x.Id); // Wait, BaseTenantEntity doesn't have an Id. I need to add it.
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.CodedValueId).IsRequired();
        
        builder.HasIndex(x => new { x.TenantId, x.CodedValueId }).IsUnique();
        
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.Name).HasMaxLength(255);
    }
}
