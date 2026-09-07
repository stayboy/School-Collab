using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (WS-A1). The <c>AssignmentId</c> FK is declared
/// from the aggregate side in <c>AssignmentConfiguration</c> (cascade +
/// auto-include + field access) — NOT here — so the dependent-side
/// configuration stays free of relationship declarations and the
/// single-source-of-truth declaration lives next to the other
/// <see cref="Assignment"/> navigation declarations.
/// </summary>
internal sealed class ContentModuleConfiguration : TenantEntityTypeConfigurationBase<ContentModule>
{
    public ContentModuleConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<ContentModule> builder)
    {
        builder.ToTable("assignment_content_modules");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.AssignmentId).IsRequired();
        builder.Property(x => x.ModuleType).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Url).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.StoragePath).HasMaxLength(500);
        builder.Property(x => x.DisplayOrder).IsRequired();
        builder.Property(x => x.MinCompletionThresholdPercent).IsRequired().HasDefaultValue(100);
        builder.Property(x => x.IsRequired).IsRequired().HasDefaultValue(false);

        builder.HasIndex(x => new { x.TenantId, x.AssignmentId })
            .HasDatabaseName("ix_assignment_content_modules_tenant_assignment");
    }
}
