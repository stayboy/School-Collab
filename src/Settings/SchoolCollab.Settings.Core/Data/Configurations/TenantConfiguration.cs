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
    }
}