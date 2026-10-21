using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Configurations;

internal sealed class TemplateConstraintConfiguration
    : TenantOrGlobalEntityTypeConfigurationBase<TemplateConstraint>
{
    public TemplateConstraintConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    protected override void ConfigureHybridEntity(EntityTypeBuilder<TemplateConstraint> builder)
    {
        builder.ToTable("template_constraints");

        builder.Property(x => x.TemplateId).IsRequired();
        builder.Property(x => x.Dimension)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(x => x.Value)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(x => x.TemplateId)
            .HasDatabaseName("ix_template_constraints_template");

        // Relationship (FK + cascade) is declared once, on the principal side in
        // TenantNotificationTemplateConfiguration — where the mapped collection
        // navigation lives.
    }
}