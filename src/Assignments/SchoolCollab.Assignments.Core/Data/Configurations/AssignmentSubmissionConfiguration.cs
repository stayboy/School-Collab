using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

internal sealed class AssignmentSubmissionConfiguration : TenantEntityTypeConfigurationBase<AssignmentSubmission>
{
    public AssignmentSubmissionConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<AssignmentSubmission> builder)
    {
        builder.ToTable("assignment_submissions");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.AssignmentId).IsRequired();
        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.CurrentVersionNumber).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CurrentSource).IsRequired().HasDefaultValue(SubmissionSource.Student);
        builder.Property(x => x.SubmittedByGuardianId);
        builder.Property(x => x.LastSubmittedAt).IsRequired();
        builder.Property(x => x.SubmissionGateId);
        builder.Property(x => x.ReviewState).IsRequired().HasDefaultValue(ReviewState.Pending);

        // ── WS-A3 (spec §7 Q4): teacher override columns ────────────────
        // When AttemptLimitOverriddenAt is non-null the submission's
        // MaxAttempts cap is cleared (permanent for the submission;
        // raising MaxAttempts on a draft assignment also helps via the
        // standard edit path). AttemptLimitOverriddenBy records the
        // approver — identity placeholder posture (D-6).
        builder.Property(x => x.AttemptLimitOverriddenAt);
        builder.Property(x => x.AttemptLimitOverriddenBy);

        // One current submission per (assignment, student) (spec §4.11 / §5).
        builder.HasIndex(x => new { x.TenantId, x.AssignmentId, x.StudentId })
            .IsUnique()
            .HasDatabaseName("uq_assignment_submissions_tenant_assignment_student");
    }
}
