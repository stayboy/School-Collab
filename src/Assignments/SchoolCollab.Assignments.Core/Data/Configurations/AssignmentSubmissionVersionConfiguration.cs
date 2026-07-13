using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

internal sealed class AssignmentSubmissionVersionConfiguration : TenantEntityTypeConfigurationBase<AssignmentSubmissionVersion>
{
    public AssignmentSubmissionVersionConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<AssignmentSubmissionVersion> builder)
    {
        builder.ToTable("assignment_submission_versions");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.SubmissionId).IsRequired();
        builder.Property(x => x.AssignmentId).IsRequired();
        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.VersionNumber).IsRequired();
        builder.Property(x => x.Source).IsRequired();
        builder.Property(x => x.SubmittedByGuardianId);
        builder.Property(x => x.SubmittedAt).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(20000);

        // One version row per (submission, version number) (spec §4.11 / §5).
        builder.HasIndex(x => new { x.TenantId, x.SubmissionId, x.VersionNumber })
            .IsUnique()
            .HasDatabaseName("uq_assignment_submission_versions_tenant_submission_version");
    }
}
