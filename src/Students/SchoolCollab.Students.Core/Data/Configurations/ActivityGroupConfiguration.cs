using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). The named "Tenant"
/// filter scopes queries to the current tenant. Case-insensitive unique name
/// per tenant is enforced by a partial unique index on (tenant_id, lower(name))
/// created via raw SQL in the migration — EF Core cannot express the lower()
/// expression (mirrors the CodedValue COALESCE pattern).
/// </summary>
internal sealed class ActivityGroupConfiguration : TenantEntityTypeConfigurationBase<ActivityGroup>
{
    public ActivityGroupConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<ActivityGroup> builder)
    {
        builder.ToTable("activity_groups");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.Property(x => x.Category)
            .HasMaxLength(100);

        builder.Property(x => x.PeriodId).IsRequired(false);
        builder.Property(x => x.Capacity).IsRequired(false);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasDefaultValue(ActivityGroupStatus.Active);

        // Optional FK → periods.id (FR-4). A group MAY outlast its period.
        builder.HasOne<Period>()
            .WithMany()
            .HasForeignKey(x => x.PeriodId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // FR-1 / AC-3: case-insensitive unique name per tenant. The actual
        // unique index on (tenant_id, lower(name)) is created via raw SQL in the
        // migration because EF Core cannot express the lower() expression. This
        // non-unique helper index is tracked by the EF model snapshot; the raw
        // SQL index (same name) supersedes it at the DB level.
        builder.HasIndex(x => new { x.TenantId, x.Name })
            .HasDatabaseName("ix_activity_groups_tenant_name");

        // NFR-3 hot paths (tenant_id leading).
        builder.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("ix_activity_groups_tenant_status");

        builder.HasIndex(x => new { x.TenantId, x.PeriodId })
            .HasDatabaseName("ix_activity_groups_tenant_period");

        builder.Ignore(x => x.DomainEvents);
    }
}
