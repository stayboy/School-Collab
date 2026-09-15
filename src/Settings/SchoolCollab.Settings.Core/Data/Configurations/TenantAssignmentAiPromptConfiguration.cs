using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

/// <summary>
/// Configures <see cref="TenantAssignmentAiPrompt"/> — one row per tenant,
/// soft-delete aware, PostgreSQL xmin row version. Mirrors
/// <see cref="TenantAssignmentPolicyConfiguration"/> (WS-C1) and
/// <see cref="TenantSignatureConsentTextConfiguration"/> (WS-C2). WS-B2 /
/// spec §3.4.
/// </summary>
internal sealed class TenantAssignmentAiPromptConfiguration
    : TenantEntityTypeConfigurationBase<TenantAssignmentAiPrompt>
{
    public TenantAssignmentAiPromptConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<TenantAssignmentAiPrompt> builder)
    {
        builder.ToTable("tenant_assignment_ai_prompts");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.SystemPrompt).HasMaxLength(TenantAssignmentAiPrompt.MaxSystemPromptLength);

        // One organization-AI-prompt row per tenant.
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasDatabaseName("ix_tenant_assignment_ai_prompts_tenant")
            .HasFilter("is_deleted = false");
    }
}
