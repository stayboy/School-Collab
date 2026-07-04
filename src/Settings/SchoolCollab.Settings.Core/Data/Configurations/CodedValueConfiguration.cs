using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

internal sealed class CodedValueConfiguration : EntityTypeConfigurationBase<CodedValue>
{
    protected override void ConfigureEntity(EntityTypeBuilder<CodedValue> builder)
    {
        builder.ToTable("coded_values");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(100);

        // Unique index on (ParentId, Code) is created via raw SQL in the migration
        // because EF Core cannot express COALESCE(ParentId, sentinel) for NULL handling.
        // Root values (ParentId IS NULL) share a synthetic sentinel so their codes
        // remain unique among other roots, while child codes are scoped to their parent.
        builder.HasIndex(x => x.Code)
            .HasDatabaseName("ix_coded_values_code");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        builder.Property(x => x.DisplayOrder)
            .HasDefaultValue(0);

        builder.Property(x => x.IsDisabled)
            .HasDefaultValue(false);


        builder.HasOne<CodedValue>()
            .WithMany()
            .HasForeignKey(x => x.ParentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ParentId)
            .HasDatabaseName("ix_coded_values_parent_id");

        builder.Ignore(x => x.DomainEvents);
        builder.Navigation(x => x.Attributes).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.Navigation(x => x.AttributeDefinitions).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.OwnsMany(x => x.Attributes, attr =>
        {
            attr.ToTable("coded_value_attributes");
            attr.WithOwner().HasForeignKey(a => a.CodedValueId);
            attr.HasKey(a => new { a.CodedValueId, a.Key });
            attr.Property(a => a.Key).HasMaxLength(100).IsRequired();
            attr.Property(a => a.Value).HasMaxLength(500).IsRequired();

            attr.HasIndex(a => new { a.Key, a.Value })
                .HasDatabaseName("ix_coded_value_attributes_key_value");
        });

        builder.OwnsMany(x => x.AttributeDefinitions, def =>
        {
            def.ToTable("coded_value_attribute_definitions");
            def.WithOwner().HasForeignKey(d => d.CodedValueId);
            def.HasKey(d => new { d.CodedValueId, d.Key });
            def.Property(d => d.Key).HasMaxLength(100).IsRequired();
            def.Property(d => d.DisplayName).HasMaxLength(200);
            def.Property(d => d.DataType).IsRequired().HasDefaultValue(Domain.AttributeDataType.Text);
            def.Property(d => d.SourceCode).HasMaxLength(100);
            def.Property(d => d.IsRequired).IsRequired().HasDefaultValue(false);
            def.Property(d => d.AllowMultiple).IsRequired().HasDefaultValue(false);
            def.Property(d => d.MinLength);
            def.Property(d => d.MaxLength);
            def.Property(d => d.RegexPattern).HasMaxLength(500);

            def.HasIndex(d => d.Key)
                .HasDatabaseName("ix_coded_value_attribute_definitions_key");
        });
    }
}
