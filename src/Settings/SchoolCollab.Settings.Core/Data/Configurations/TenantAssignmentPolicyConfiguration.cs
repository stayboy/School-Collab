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

        // The shared AssignmentPolicyFields shape, stored as real nullable columns (no JSON).
        // A null column means the tenant has not set that field; the built-in default applies.
        builder.Property(x => x.SignatureRequirement)
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(x => x.RequiresApprovalBeforePublish);
        builder.Property(x => x.MaxPrimaryContacts);
        builder.Property(x => x.MaxCopyContacts);

        // One policy row per tenant.
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasDatabaseName("ix_tenant_assignment_policies_tenant")
            .HasFilter("is_deleted = false");
    }
}
