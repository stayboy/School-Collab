using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). Append-only contact
/// change audit row — never updated or deleted, so no row version is mapped.
/// </summary>
internal sealed class ContactAuditEntryConfiguration : TenantEntityTypeConfigurationBase<ContactAuditEntry>
{
    public ContactAuditEntryConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<ContactAuditEntry> builder)
    {
        builder.ToTable("contact_audit_entries");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.ContactId).IsRequired();
        builder.Property(x => x.OwnerType).IsRequired();
        builder.Property(x => x.OwnerId).IsRequired();
        builder.Property(x => x.ChangeKind)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(x => x.PreviousChannel).IsRequired();
        builder.Property(x => x.PreviousValue).IsRequired();
        builder.Property(x => x.PreviousLabel);
        builder.Property(x => x.PreviousCountryCode);

        builder.Property(x => x.NewChannel);
        builder.Property(x => x.NewValue);
        builder.Property(x => x.NewLabel);
        builder.Property(x => x.NewCountryCode);

        builder.Property(x => x.Reason)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(x => x.ActorId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ActorDisplayName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.OccurredAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.ContactId })
            .HasDatabaseName("ix_contact_audit_entries_tenant_contact");

        builder.HasIndex(x => new { x.TenantId, x.OwnerType, x.OwnerId, x.OccurredAt })
            .HasDatabaseName("ix_contact_audit_entries_tenant_owner_occurred");
    }
}
