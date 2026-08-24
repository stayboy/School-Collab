using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Read-model table for locally-replicated coded values
/// (adr-cross-module-calls.md Phase 1).
///
/// <para><b>No "Tenant" query filter:</b> unlike domain entities this table is
/// read across tenancy scopes on purpose — global blueprint rows carry
/// <c>TenantId = null</c> and must be visible to every tenant. Tenancy is
/// resolved explicitly by <c>ILocalCodedValueRepository</c>, not by an EF
/// filter (same allow-list rationale as OutboxMessage).</para>
/// </summary>
internal sealed class LocalCodedValueConfiguration : IEntityTypeConfiguration<LocalCodedValue>
{
    public void Configure(EntityTypeBuilder<LocalCodedValue> builder)
    {
        builder.ToTable("local_coded_values");

        builder.HasKey(x => x.RowId);

        // One global row per coded value + at most one overlay per tenant.
        builder.HasIndex(x => new { x.TenantId, x.Id })
            .IsUnique()
            .HasDatabaseName("ix_local_coded_values_tenant_id");
        builder.HasIndex(x => x.Code)
            .HasDatabaseName("ix_local_coded_values_code");

        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.ParentCode).HasMaxLength(100);

        // Attribute list stored as jsonb; only deserialization matters to the
        // projection (no SQL-side queries against attributes), so a value
        // converter is sufficient.
        var attributesConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<LocalCodedValueAttribute>, string>(
            v => System.Text.Json.JsonSerializer.Serialize(v, JsonOptions),
            v => System.Text.Json.JsonSerializer.Deserialize<List<LocalCodedValueAttribute>>(v, JsonOptions) ?? new List<LocalCodedValueAttribute>());
        builder.Property(x => x.Attributes)
            .HasConversion(attributesConverter)
            .HasColumnType("jsonb");
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);
}
