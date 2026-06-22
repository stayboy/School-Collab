using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class StudentConfiguration : TenantEntityTypeConfigurationBase<Student>
{
    public StudentConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<Student> builder)
    {
        builder.ToTable("students");

        builder.ConfigureAuditProperties();
        builder.ConfigureSoftDeleteProperties();
        builder.ConfigureSoftDeleteQueryFilter();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.StudentNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.FirstName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.LastName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.DateOfBirth);

        builder.Property(x => x.GenderCodedValueId);

        builder.Property(x => x.ContactEmail)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.ContactPhone)
            .HasMaxLength(50);

        builder.HasIndex(x => x.StudentNumber)
            .IsUnique()
            .HasDatabaseName("ix_students_student_number");

        builder.HasIndex(x => x.GenderCodedValueId)
            .HasDatabaseName("ix_students_gender_cv_id");

        builder.HasIndex(x => x.IsDeleted)
            .HasDatabaseName("ix_students_is_deleted");

        builder.HasIndex(x => new { x.TenantId, x.IsDeleted })
            .HasDatabaseName("ix_students_tenant_id_is_deleted");

        builder.Ignore(x => x.DomainEvents);
    }
}