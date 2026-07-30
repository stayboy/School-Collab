using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). The named "Tenant" filter
/// scopes queries to the current tenant. The <c>CodedValueId</c> unique index is
/// composite <c>(tenant_id, coded_value_id)</c> (FR-7) — one GradeLevel per
/// (tenant, coded value).
/// </summary>
internal sealed class GradeLevelConfiguration : TenantEntityTypeConfigurationBase<GradeLevel>
{
    public GradeLevelConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<GradeLevel> builder)
    {
        builder.ToTable("grade_levels");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.CodedValueId).IsRequired();
        builder.Property(x => x.Level).IsRequired();
        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(x => x.DisplayOrder).IsRequired();

        // Enrollment validation guard clauses (§2 of plan):
        builder.Property(x => x.MinAge)
            .HasColumnName("min_age")
            .IsRequired(false);

        builder.Property(x => x.MaxAge)
            .HasColumnName("max_age")
            .IsRequired(false);

        builder.Property(x => x.AllowedGenderCodedValueId)
            .HasColumnName("allowed_gender_coded_value_id")
            .IsRequired(false);

        // FR-7: unique per (tenant, coded_value) — was globally unique on CodedValueId.
        builder.HasIndex(x => new { x.TenantId, x.CodedValueId })
            .IsUnique()
            .HasDatabaseName("ix_grade_levels_tenant_coded_value_id");

        builder.HasIndex(x => x.Level)
            .HasDatabaseName("ix_grade_levels_level");

        builder.Ignore(x => x.DomainEvents);
    }
}
