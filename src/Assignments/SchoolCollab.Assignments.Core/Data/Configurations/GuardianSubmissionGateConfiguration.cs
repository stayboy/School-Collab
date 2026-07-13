using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

internal sealed class GuardianSubmissionGateConfiguration : TenantEntityTypeConfigurationBase<GuardianSubmissionGate>
{
    public GuardianSubmissionGateConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<GuardianSubmissionGate> builder)
    {
        builder.ToTable("guardian_submission_gates");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.AssignmentId).IsRequired();
        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.ReviewedByGuardianId);
        builder.Property(x => x.ReviewedAt);
        builder.Property(x => x.ReviewComment).HasMaxLength(2000);
        builder.Property(x => x.SubmissionEnabledForStudent).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.SubmittedByGuardianId);
        builder.Property(x => x.SubmittedByGuardianAt);

        // One gate per (assignment, student) (spec §4.10 / §5).
        builder.HasIndex(x => new { x.TenantId, x.AssignmentId, x.StudentId })
            .IsUnique()
            .HasDatabaseName("uq_guardian_submission_gates_tenant_assignment_student");
    }
}
