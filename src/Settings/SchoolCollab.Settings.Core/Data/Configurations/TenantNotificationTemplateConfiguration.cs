using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

internal sealed class TenantNotificationTemplateConfiguration
    : TenantEntityTypeConfigurationBase<TenantNotificationTemplate>
{
    public TenantNotificationTemplateConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TenantNotificationTemplate> builder)
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

        // One template per tenant per kind × channel.
        builder.HasIndex(x => new { x.TenantId, x.Kind, x.Channel })
            .IsUnique()
            .HasDatabaseName("ix_tenant_notification_templates_tenant_kind_channel")
            .HasFilter("is_deleted = false");

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