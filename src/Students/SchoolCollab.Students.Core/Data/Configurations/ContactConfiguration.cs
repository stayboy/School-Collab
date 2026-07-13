using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Multi-channel contact owned by a student or guardian (spec §4.4). Soft-deletable.
/// </summary>
internal sealed class ContactConfiguration : TenantEntityTypeConfigurationBase<Contact>
{
    public ContactConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.OwnerType).IsRequired();
        builder.Property(x => x.OwnerId).IsRequired();
        builder.Property(x => x.Channel).IsRequired();
        builder.Property(x => x.Value).IsRequired();
        builder.Property(x => x.Label);
        builder.Property(x => x.IsPrimary).HasDefaultValue(false);
        builder.Property(x => x.IsVerified).HasDefaultValue(false);

        builder.HasIndex(x => new { x.TenantId, x.OwnerType, x.OwnerId, x.Channel, x.Value })
            .IsUnique()
            .HasDatabaseName("ix_contacts_tenant_owner_channel_value");
        builder.HasIndex(x => new { x.TenantId, x.OwnerType, x.OwnerId })
            .HasDatabaseName("ix_contacts_tenant_owner");

        builder.Ignore(x => x.Subscriptions);
    }
}
