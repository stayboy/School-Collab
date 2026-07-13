using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Append-only guardian name history (spec §4.2). Tenant-scoped (spec §5).
/// </summary>
internal sealed class GuardianNameHistoryConfiguration : TenantEntityTypeConfigurationBase<GuardianNameHistory>
{
    public GuardianNameHistoryConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<GuardianNameHistory> builder)
    {
        builder.ToTable("guardian_name_history");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.GuardianId).IsRequired();
        builder.Property(x => x.FirstName).IsRequired();
        builder.Property(x => x.LastName).IsRequired();
        builder.Property(x => x.DisplayName);

        builder.HasIndex(x => new { x.TenantId, x.GuardianId })
            .HasDatabaseName("ix_guardian_name_history_tenant_guardian");
    }
}
