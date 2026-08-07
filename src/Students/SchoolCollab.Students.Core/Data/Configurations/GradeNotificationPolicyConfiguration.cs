using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Optional per-grade notification policy. Strict tenant-scoped, one row per
/// (tenant, grade). Channel lists are nullable jsonb (null = inherit the tenant
/// default). Cascade-deletes with its grade level.
/// </summary>
internal sealed class GradeNotificationPolicyConfiguration
    : TenantEntityTypeConfigurationBase<GradeNotificationPolicy>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public GradeNotificationPolicyConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    private static readonly Expression<Func<NotificationChannel[]?, string?>> ToJson =
        v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions);

    private static readonly Expression<Func<string?, NotificationChannel[]?>> FromJson =
        s => s == null ? null : JsonSerializer.Deserialize<NotificationChannel[]>(s, JsonOptions);

    protected override void ConfigureTenantEntity(EntityTypeBuilder<GradeNotificationPolicy> builder)
    {
        builder.ToTable("grade_notification_policies");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.GradeLevelId).IsRequired();

        builder.Property(x => x.PreferredChannelOrder)
            .HasColumnType("jsonb")
            .HasConversion(ToJson, FromJson);

        builder.Property(x => x.BlockedChannels)
            .HasColumnType("jsonb")
            .HasConversion(ToJson, FromJson);

        builder.Property(x => x.MaxNotifications);
        builder.Property(x => x.MaxReminders);
        builder.Property(x => x.ReminderIntervalHours);
        builder.Property(x => x.LinkValidityDays);
        builder.Property(x => x.SendoutTimeOfDay);
        builder.Property(x => x.SendoutIntervalMinutes);

        // One policy row per (tenant, grade); cascade-delete with the grade.
        builder.HasOne<GradeLevel>()
            .WithMany()
            .HasForeignKey(x => x.GradeLevelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.GradeLevelId })
            .IsUnique()
            .HasDatabaseName("ix_grade_notification_policies_tenant_grade")
            .HasFilter("is_deleted = false");
    }
}
