using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.CodedValues.Core.Domain;

namespace SchoolCollab.CodedValues.Core.Data.Configurations;

internal sealed class CodedValueConfiguration : IEntityTypeConfiguration<CodedValue>
{
    public void Configure(EntityTypeBuilder<CodedValue> builder)
    {
        builder.ToTable("coded_values");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(x => x.Code)
            .IsUnique()
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

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.Property(x => x.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

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

            def.HasIndex(d => d.Key)
                .HasDatabaseName("ix_coded_value_attribute_definitions_key");
        });
    }
}
