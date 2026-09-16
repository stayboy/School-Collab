using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

internal sealed class AssignmentRecipientConfiguration : TenantEntityTypeConfigurationBase<AssignmentRecipient>
{
    public AssignmentRecipientConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<AssignmentRecipient> builder)
    {
        builder.ToTable("assignment_recipients");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.AssignmentId).IsRequired();
        builder.Property(x => x.OwnerType).IsRequired();
        builder.Property(x => x.OwnerId).IsRequired();
        builder.Property(x => x.WardStudentId);
        builder.Property(x => x.ContactId).IsRequired();
        builder.Property(x => x.Channel).IsRequired();
        builder.Property(x => x.Role);
        builder.Property(x => x.NotifyOnBroadcast).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.SubscriptionActive).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.DeliveredAt);
        builder.Property(x => x.OpenedAt);
        // WS-E1 (ar-14-deep-links): protected deep-link token + expiry per recipient.
        // Length accommodates the DataProtection ciphertext (+ JSON payload overhead).
        builder.Property(x => x.DeepLinkToken).HasMaxLength(4096);
        builder.Property(x => x.DeepLinkExpiresAt);

        // One recipient row per contact per assignment (spec §4.6 / §5).
        builder.HasIndex(x => new { x.TenantId, x.AssignmentId, x.ContactId })
            .IsUnique()
            .HasDatabaseName("uq_assignment_recipients_tenant_assignment_contact");

        builder.HasIndex(x => new { x.AssignmentId, x.OwnerId })
            .HasDatabaseName("ix_assignment_recipients_assignment_owner");
    }
}
