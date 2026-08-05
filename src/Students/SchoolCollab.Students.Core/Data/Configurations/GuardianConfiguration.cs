using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Guardian aggregate (spec §4.1). Soft-deletable (block only).
/// </summary>
internal sealed class GuardianConfiguration : TenantEntityTypeConfigurationBase<Guardian>
{
    public GuardianConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<Guardian> builder)
    {
        builder.ToTable("guardians");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.TitleCodedValueId);
        builder.Property(x => x.FirstName).IsRequired();
        builder.Property(x => x.LastName).IsRequired();
        builder.Property(x => x.DisplayName);
        builder.Property(x => x.DateOfBirth);
        builder.Property(x => x.GenderCodedValueId);
        builder.Property(x => x.Address);
        builder.Property(x => x.CommunityId);

        builder.HasIndex(x => new { x.TenantId, x.LastName })
            .HasDatabaseName("ix_guardians_tenant_last_name");

        builder.Ignore(x => x.NameHistory);
        builder.Ignore(x => x.DomainEvents);
    }
}
