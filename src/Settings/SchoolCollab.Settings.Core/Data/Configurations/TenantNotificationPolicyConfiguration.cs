using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

internal sealed class TenantNotificationPolicyConfiguration
    : TenantEntityTypeConfigurationBase<TenantNotificationPolicy>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public TenantNotificationPolicyConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TenantNotificationPolicy> builder)
    {
        builder.ToTable("tenant_notification_policies");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.PreferredChannelOrder)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                s => string.IsNullOrEmpty(s) ? Array.Empty<NotificationChannel>() : JsonSerializer.Deserialize<NotificationChannel[]>(s, JsonOptions) ?? Array.Empty<NotificationChannel>())
            .IsRequired();

        builder.Property(x => x.BlockedChannels)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                s => string.IsNullOrEmpty(s) ? Array.Empty<NotificationChannel>() : JsonSerializer.Deserialize<NotificationChannel[]>(s, JsonOptions) ?? Array.Empty<NotificationChannel>())
            .IsRequired();

        builder.Property(x => x.MaxNotifications);
        builder.Property(x => x.MaxReminders);
        builder.Property(x => x.ReminderIntervalHours);
        builder.Property(x => x.LinkValidityDays);
        builder.Property(x => x.SendoutTimeOfDay);
        builder.Property(x => x.SendoutIntervalMinutes);

        // One policy row per tenant.
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasDatabaseName("ix_tenant_notification_policies_tenant")
            .HasFilter("is_deleted = false");
    }
}
