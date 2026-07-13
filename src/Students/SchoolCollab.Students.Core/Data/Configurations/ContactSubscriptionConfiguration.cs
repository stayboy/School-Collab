using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Contact subscription (spec §4.5). Not soft-deletable (unsubscribe flips status).
/// </summary>
internal sealed class ContactSubscriptionConfiguration : TenantEntityTypeConfigurationBase<ContactSubscription>
{
    public ContactSubscriptionConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<ContactSubscription> builder)
    {
        builder.ToTable("contact_subscriptions");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.ContactId).IsRequired();
        builder.Property(x => x.Scope).IsRequired();
        builder.Property(x => x.ScopeRefId);
        builder.Property(x => x.Status).IsRequired().HasDefaultValue(SubscriptionStatus.Unsubscribed);

        builder.HasIndex(x => new { x.TenantId, x.ContactId, x.Scope, x.ScopeRefId })
            .IsUnique()
            .HasDatabaseName("ix_contact_subscriptions_tenant_contact_scope");
    }
}
