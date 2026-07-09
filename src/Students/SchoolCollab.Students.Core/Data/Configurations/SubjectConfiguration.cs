using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). Unique indexes on
/// <c>code</c> and <c>coded_value_id</c> are composite <c>(tenant_id, …)</c> (FR-7).
/// </summary>
internal sealed class SubjectConfiguration : TenantEntityTypeConfigurationBase<Subject>
{
    public SubjectConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("subjects");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.CodedValueId).IsRequired();

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.DisplayOrder).IsRequired();

        // FR-7: unique per (tenant, code) and (tenant, coded_value) — were global.
        builder.HasIndex(x => new { x.TenantId, x.Code })
            .IsUnique()
            .HasDatabaseName("ix_subjects_tenant_code");

        builder.HasIndex(x => new { x.TenantId, x.CodedValueId })
            .IsUnique()
            .HasDatabaseName("ix_subjects_tenant_coded_value_id");

        builder.Ignore(x => x.DomainEvents);
    }
}
