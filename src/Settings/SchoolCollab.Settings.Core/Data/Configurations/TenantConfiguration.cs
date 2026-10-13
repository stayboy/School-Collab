using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Data.Configurations;

/// <summary>
/// EF Core mapping for the global <see cref="Tenant"/> aggregate. Tenant is
/// <b>not</b> tenant-scoped (it does not implement <see cref="ITenantEntity"/>),
/// so no global query filter is applied — every tenant row is visible to the dev
/// switcher and the seeder regardless of the active tenant context. Unique on the
/// natural key <see cref="Tenant.Name"/> to keep seeding idempotent by name.
/// <para>
/// <see cref="Tenant.IsDefault"/> carries a <b>partial</b> unique index so PostgreSQL
/// — not application code — enforces "at most one platform default". The filter emits
/// <c>WHERE is_default</c>, so any number of rows may sit at <c>false</c> while a second
/// <c>true</c> is rejected at the database.
/// </para>
/// </summary>
internal sealed class TenantConfiguration : EntityTypeConfigurationBase<Tenant>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue(TenantType.School);

        // Idempotent-by-name seeding relies on this unique index: a re-run of the
        // seeder finds the existing row by Name rather than inserting a duplicate.
        builder.HasIndex(x => x.Name)
            .IsUnique()
            .HasDatabaseName("ix_tenants_name_unique");

        // Designated platform default for the admin portal (at most one row).
        // Fail-closed base default: a publish never bakes a default in, so every
        // existing row and every fresh insert starts at false until designated.
        builder.Property(x => x.IsDefault)
            .IsRequired()
            .HasDefaultValue(false);

        // Partial unique index: PostgreSQL enforces the "at most one default"
        // invariant, so two concurrent designations cannot both win.
        builder.HasIndex(x => x.IsDefault)
            .IsUnique()
            .HasFilter("is_default")
            .HasDatabaseName("ix_tenants_is_default_unique");
    }
}