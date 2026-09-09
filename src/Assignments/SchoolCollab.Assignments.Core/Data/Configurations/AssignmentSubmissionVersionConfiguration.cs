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

        // ── WS-A3 (spec §3.3): auto-scored total + pass flag ────────────
        // Set at creation by the submission handler scoring path. Both
        // nullable — TeacherGraded submissions leave them null, and a
        // missing PassScore on the assignment leaves Passed null.
        builder.Property(x => x.Score).HasPrecision(5, 2);
        builder.Property(x => x.Passed);

        // ── WS-A3: answers cascade from the version side (no navs) ───────
        // The ar-4 standalone-entity precedent: declaration lives once
        // on the aggregate side, navigation-less, cascade-on-delete so
        // a removed version drops its answer rows. No
        // AutoInclude() — answers are loaded by SubmissionVersionId when
        // needed (per-version analytics read path).
        builder.HasMany<SubmissionAnswer>().WithOne()
            .HasForeignKey(a => a.SubmissionVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        // One version row per (submission, version number) (spec §4.11 / §5).
        builder.HasIndex(x => new { x.TenantId, x.SubmissionId, x.VersionNumber })
            .IsUnique()
            .HasDatabaseName("uq_assignment_submission_versions_tenant_submission_version");
    }
}
