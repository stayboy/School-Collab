using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

internal sealed class TenantAssignmentPolicyConfiguration
    : TenantEntityTypeConfigurationBase<TenantAssignmentPolicy>
{
    public TenantAssignmentPolicyConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TenantAssignmentPolicy> builder)
    {
        builder.ToTable("tenant_assignment_policies");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.RequiresSignatureDefault).IsRequired();

        // One policy row per tenant.
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasDatabaseName("ix_tenant_assignment_policies_tenant")
            .HasFilter("is_deleted = false");
    }
}
