using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

/// <summary>
/// EF Core mapping for <see cref="AdminUserPreference"/> — a global (non-tenant-scoped)
/// row holding one operator's preferred default tenant for the admin portal.
/// </summary>
/// <remarks>
/// No tenant query filter and no <c>TenantId</c> column: the row is keyed by the OIDC
/// subject, and its <c>DefaultTenantId</c> is a value. The unique index on
/// <see cref="AdminUserPreference.SubjectId"/> is what makes the handler's upsert
/// idempotent under concurrency, mirroring <c>ix_tenants_name_unique</c>.
/// </remarks>
internal sealed class AdminUserPreferenceConfiguration : EntityTypeConfigurationBase<AdminUserPreference>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AdminUserPreference> builder)
    {
        builder.ToTable("admin_user_preferences");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.SubjectId)
            .IsRequired()
            .HasMaxLength(AdminUserPreference.MaxSubjectIdLength);

        builder.Property(x => x.DefaultTenantId);

        builder.HasIndex(x => x.SubjectId)
            .IsUnique()
            .HasDatabaseName("ix_admin_user_preferences_subject_id_unique");
    }
}
