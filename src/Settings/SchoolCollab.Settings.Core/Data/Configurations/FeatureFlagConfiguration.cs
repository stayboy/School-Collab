using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

internal sealed class FeatureFlagConfiguration : EntityTypeConfigurationBase<FeatureFlag>
{
    protected override void ConfigureEntity(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("feature_flags");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.Key)
            .IsRequired()
            .HasMaxLength(200);

        // Unique on the non-deleted key. Soft-deleted rows keep their key so a
        // recovered flag lands on the same row; the partial index prevents a
        // second live flag from reusing the key while the original is deleted.
        builder.HasIndex(x => x.Key)
            .IsUnique()
            .HasDatabaseName("ix_feature_flags_key_unique")
            .HasFilter("is_deleted = false");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasMaxLength(1000);

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue(FlagKind.Boolean);

        builder.Property(x => x.IsEnabled)
            .HasDefaultValue(false);

        builder.Property(x => x.IsArchived)
            .HasDefaultValue(false);
    }
}