using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (WS-A1). Same FK declaration pattern as
/// <see cref="ContentModuleConfiguration"/> — the <c>AssignmentId</c>
/// relationship is declared from the aggregate side in
/// <c>AssignmentConfiguration</c>.
/// </summary>
internal sealed class AssignmentResourceConfiguration : TenantEntityTypeConfigurationBase<AssignmentResource>
{
    public AssignmentResourceConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<AssignmentResource> builder)
    {
        builder.ToTable("assignment_resources");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.AssignmentId).IsRequired();
        builder.Property(x => x.ResourceKind).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(2000);
        builder.Property(x => x.StoragePath).HasMaxLength(500);
        builder.Property(x => x.DisplayName).HasMaxLength(200);
        builder.Property(x => x.IncludedInGeneration).IsRequired().HasDefaultValue(true);

        builder.HasIndex(x => new { x.TenantId, x.AssignmentId })
            .HasDatabaseName("ix_assignment_resources_tenant_assignment");
    }
}
