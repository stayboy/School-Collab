using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

/// <summary>
/// WS-A3 (spec §3.3): strict tenant entity for one structured answer
/// row per (submission version, question). Same FK declaration pattern
/// as <see cref="ContentModuleConfiguration"/> — the
/// <c>SubmissionVersionId</c> relationship is declared once from the
/// aggregate side in <c>AssignmentSubmissionVersionConfiguration</c>
/// (cascade + auto-include NOT applied — answers are loaded by
/// <c>SubmissionVersionId</c> directly, not via a navigation). No
/// navigation properties on either side (single-source-of-truth
/// declaration lives next to the other
/// <see cref="AssignmentSubmissionVersion"/> relationship declarations).
/// </summary>
internal sealed class SubmissionAnswerConfiguration : TenantEntityTypeConfigurationBase<SubmissionAnswer>
{
    public SubmissionAnswerConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<SubmissionAnswer> builder)
    {
        builder.ToTable("assignment_submission_answers");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.SubmissionVersionId).IsRequired();
        builder.Property(x => x.QuestionId).IsRequired();
        builder.Property(x => x.SelectedOptionId);
        builder.Property(x => x.TextAnswer).HasMaxLength(2000);

        // Per-version analytics read pattern: load every answer row for
        // a given submission version. Tenant-scoped to keep the query
        // filter chain consistent with the rest of the module.
        builder.HasIndex(x => new { x.TenantId, x.SubmissionVersionId })
            .HasDatabaseName("ix_assignment_submission_answers_tenant_version");
    }
}
