using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Student↔guardian link (spec §4.3). Role only; not soft-deletable (retained on guardian soft-delete).
/// </summary>
internal sealed class StudentGuardianConfiguration : TenantEntityTypeConfigurationBase<StudentGuardian>
{
    public StudentGuardianConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<StudentGuardian> builder)
    {
        builder.ToTable("student_guardians");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.GuardianId).IsRequired();
        builder.Property(x => x.RelationshipCodedValueId);
        builder.Property(x => x.Role).IsRequired();
        builder.Property(x => x.IsEmergencyContact).HasDefaultValue(false);
        builder.Property(x => x.CreatedByGuardianId);

        builder.HasIndex(x => new { x.TenantId, x.StudentId, x.GuardianId })
            .IsUnique()
            .HasDatabaseName("ix_student_guardians_tenant_student_guardian");
        builder.HasIndex(x => new { x.TenantId, x.GuardianId })
            .HasDatabaseName("ix_student_guardians_tenant_guardian");
    }
}
