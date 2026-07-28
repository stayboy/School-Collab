using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Teacher aggregate (spec §4.12). Soft-deletable. Keeps a single staff email/phone
/// (not migrated to the <see cref="Contact"/> table).
/// </summary>
internal sealed class TeacherConfiguration : TenantEntityTypeConfigurationBase<Teacher>
{
    public TeacherConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<Teacher> builder)
    {
        builder.ToTable("teachers");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.TitleCodedValueId);
        builder.Property(x => x.FirstName).IsRequired();
        builder.Property(x => x.LastName).IsRequired();
        builder.Property(x => x.DisplayName);
        builder.Property(x => x.Email).IsRequired();
        builder.Property(x => x.ContactPhone);
        builder.Property(x => x.StaffUserId);
        builder.Property(x => x.StaffNumber).HasMaxLength(50);

        builder.HasIndex(x => new { x.TenantId, x.LastName })
            .HasDatabaseName("ix_teachers_tenant_last_name");

        builder.Ignore(x => x.Subjects);
        builder.Ignore(x => x.GradeLevels);
    }
}
