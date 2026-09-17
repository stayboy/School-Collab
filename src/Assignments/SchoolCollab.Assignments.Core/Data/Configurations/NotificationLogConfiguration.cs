using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

/// <summary>
/// WS-E2 (ar-16): strict tenant entity for per-recipient notification delivery rows.
/// The row stores its own rendered payload (decision (b)) and carries no navigation:
/// the recipient / assignment ids are denormalized so the drain and the ar-17 failure
/// query are single-context and tenant-scoped. The
/// <c>(TenantId, DeliveryStatus, NextRetryAt)</c> index is the drain's read path.
/// </summary>
internal sealed class NotificationLogConfiguration : TenantEntityTypeConfigurationBase<NotificationLog>
{
    public NotificationLogConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("notification_logs");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.AssignmentId).IsRequired();
        builder.Property(x => x.RecipientId).IsRequired();
        builder.Property(x => x.ContactId).IsRequired();
        builder.Property(x => x.Channel).IsRequired();
        builder.Property(x => x.Kind).IsRequired();
        builder.Property(x => x.Attempt).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.DeliveryStatus).IsRequired();
        builder.Property(x => x.SentAt);
        builder.Property(x => x.FailureReason).HasMaxLength(2048);
        builder.Property(x => x.NextRetryAt);

        // Decision (b): the rendered payload travels with the row so a retry
        // re-sends exactly what was queued.
        builder.Property(x => x.ToAddress).IsRequired().HasMaxLength(512);
        builder.Property(x => x.Subject).IsRequired().HasMaxLength(512);
        builder.Property(x => x.BodyHtml).IsRequired();

        // The drain's read path: due rows for a tenant, ordered by retry time.
        builder.HasIndex(x => new { x.TenantId, x.DeliveryStatus, x.NextRetryAt })
            .HasDatabaseName("ix_notification_logs_tenant_status_next_retry");

        // Read path for GET /assignments/{id}/notification-failures.
        builder.HasIndex(x => new { x.AssignmentId, x.DeliveryStatus })
            .HasDatabaseName("ix_notification_logs_assignment_status");
    }
}
