using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

internal sealed class TenantNotificationTemplateConfiguration
    : TenantOrGlobalEntityTypeConfigurationBase<TenantNotificationTemplate>
{
    public TenantNotificationTemplateConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    protected override void ConfigureHybridEntity(EntityTypeBuilder<TenantNotificationTemplate> builder)
    {
        builder.ToTable("tenant_notification_templates");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.Kind)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.Channel)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.Subject).HasMaxLength(500);
        builder.Property(x => x.Body).IsRequired().HasMaxLength(4000);

        builder.Property(x => x.Title).IsRequired().HasMaxLength(120);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Code).HasMaxLength(16);

        // Several live templates per tenant per kind × channel (round notification-templates-b2c):
        // an empty constraint set is the tenant default and each constrained row is a candidate for
        // the applicability matcher (spec §3.1.1/Q15). NON-unique — it is a lookup index now, not the
        // one-row invariant; the "one unconstrained default" rule is enforced in the create/update
        // handlers because no database constraint can express "at most one row with no children".
        builder.HasIndex(x => new { x.TenantId, x.Kind, x.Channel })
            .HasDatabaseName("ix_tenant_notification_templates_tenant_kind_channel")
            .HasFilter("is_deleted = false");

        // The generated template code is the row's exact reference (naming spec D6), so it is unique
        // per scope — the (TenantId, StudentNumber) precedent. Filtered to live rows AND to rows that
        // actually carry a code: every row that predates the code column has NULL, and a NULL is a
        // feature there (it renders as a dash), never a value to keep unique.
        builder.HasIndex(x => new { x.TenantId, x.Code })
            .HasDatabaseName("ix_tenant_notification_templates_tenant_code")
            .IsUnique()
            .HasFilter("is_deleted = false AND code IS NOT NULL");

        // Constraint rows: cascade on template deletion; field access because
        // Constraints is a read-only IReadOnlyList over the _constraints backing
        // field (EF cannot call Add on the property itself).
        builder.HasMany(x => x.Constraints)
            .WithOne()
            .HasForeignKey(c => c.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Constraints)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}