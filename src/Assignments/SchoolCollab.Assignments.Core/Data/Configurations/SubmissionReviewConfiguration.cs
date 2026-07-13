using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

internal sealed class SubmissionReviewConfiguration : TenantEntityTypeConfigurationBase<SubmissionReview>
{
    public SubmissionReviewConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<SubmissionReview> builder)
    {
        builder.ToTable("submission_reviews");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.SubmissionId).IsRequired();
        builder.Property(x => x.AssignmentId).IsRequired();
        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.TeacherId).IsRequired();
        builder.Property(x => x.Score).HasPrecision(5, 2);
        builder.Property(x => x.Grade).HasMaxLength(20);
        builder.Property(x => x.Comments).HasMaxLength(2000);
        builder.Property(x => x.ReviewedAt).IsRequired();

        // Single review row per submission (spec §4.13 / §5).
        builder.HasIndex(x => new { x.TenantId, x.SubmissionId })
            .IsUnique()
            .HasDatabaseName("uq_submission_reviews_tenant_submission");
    }
}
