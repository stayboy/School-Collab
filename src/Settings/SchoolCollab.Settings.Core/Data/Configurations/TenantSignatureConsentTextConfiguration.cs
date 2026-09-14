using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

/// <summary>
/// Configures <see cref="TenantSignatureConsentText"/> — one row per tenant,
/// soft-delete aware, PostgreSQL xmin row version. Mirrors
/// <see cref="TenantAssignmentPolicyConfiguration"/> (WS-C2 / spec §3.2).
/// </summary>
internal sealed class TenantSignatureConsentTextConfiguration
    : TenantEntityTypeConfigurationBase<TenantSignatureConsentText>
{
    public TenantSignatureConsentTextConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TenantSignatureConsentText> builder)
    {
        builder.ToTable("tenant_signature_consent_texts");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.ConsentText).HasMaxLength(TenantSignatureConsentText.MaxConsentTextLength);

        // One consent-text row per tenant.
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasDatabaseName("ix_tenant_signature_consent_texts_tenant")
            .HasFilter("is_deleted = false");
    }
}
