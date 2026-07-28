using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

/// <summary>
/// Configures <see cref="EntityCodeRule"/> as a <b>hybrid</b> tenant entity
/// (mirrors <see cref="CodedValueConfiguration"/>): nullable <c>tenant_id</c> with
/// the named "Tenant" filter <c>TenantId == CurrentTenantId OR TenantId == null</c>,
/// so shared-blueprint rules (CSV-seeded, NULL) are visible to all tenants and
/// tenant-owned rules are isolated. The <see cref="EntityCodeSegment"/> children
/// are mapped as an owned collection on a separate table (spec §4.1.1).
/// </summary>
internal sealed class EntityCodeRuleConfiguration : TenantOrGlobalEntityTypeConfigurationBase<EntityCodeRule>
{
    public EntityCodeRuleConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    protected override void ConfigureHybridEntity(EntityTypeBuilder<EntityCodeRule> builder)
    {
        builder.ToTable("entity_code_rules");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("ix_entity_code_rules_code_unique");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(false);

        // Owned collection on a separate table. Owned types inherit the owner's
        // "Tenant" filter via Include, so ValidateTenantFilters exempts them.
        builder.Navigation(x => x.Segments)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        builder.OwnsMany(x => x.Segments, seg =>
        {
            seg.ToTable("entity_code_segments");
            seg.WithOwner().HasForeignKey(s => s.EntityCodeRuleId);
            seg.HasKey(s => s.Id);
            seg.Property(s => s.Id).ValueGeneratedNever();

            seg.Property(s => s.Role).HasMaxLength(50);
            seg.Property(s => s.FixedText).HasMaxLength(200).IsRequired(false);
            seg.Property(s => s.Prefix).HasMaxLength(50).IsRequired(false);
            seg.Property(s => s.Suffix).HasMaxLength(50).IsRequired(false);
            seg.Property(s => s.UpperLimit).HasMaxLength(20).IsRequired(false);
            seg.Property(s => s.LastPrefix).HasMaxLength(10).IsRequired(false);
            seg.Property(s => s.LastPeriodBucket).HasMaxLength(20).IsRequired(false);

            // A segment index is unique within its rule.
            seg.HasIndex(s => new { s.EntityCodeRuleId, s.Index })
                .IsUnique()
                .HasDatabaseName("ix_entity_code_segments_rule_index_unique");
        });
    }
}