using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

/// <summary>
/// R3 (D4/P1-2) — the append-only generation header. Strict tenant entity; the
/// <c>assignment_id</c> FK is CASCADE (deleting an assignment removes its generation history) and
/// there is no aggregate-side navigation, matching the <see cref="SignatureEventConfiguration"/>
/// standalone-row posture.
/// <para>The index is the R3 criterion-8 module convention: <c>(TenantId, AssignmentId)</c>, i.e. the
/// same tenant-plus-parent shape every other child table in this module carries.</para>
/// </summary>
internal sealed class AssignmentQuestionGenerationConfiguration
    : TenantEntityTypeConfigurationBase<AssignmentQuestionGeneration>
{
    public AssignmentQuestionGenerationConfiguration(Expression<Func<Guid>> tenantIdAccessor)
        : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<AssignmentQuestionGeneration> builder)
    {
        builder.ToTable("assignment_question_generations");

        builder.Property(x => x.AssignmentId).IsRequired();
        builder.Property(x => x.QuestionCount).IsRequired();
        builder.Property(x => x.Types).HasMaxLength(100);
        builder.Property(x => x.DifficultyEasyCount);
        builder.Property(x => x.DifficultyMediumCount);
        builder.Property(x => x.DifficultyHardCount);
        builder.Property(x => x.Provider).HasMaxLength(50);
        builder.Property(x => x.Model).IsRequired().HasMaxLength(200);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasOne<Assignment>()
            .WithMany()
            .HasForeignKey(x => x.AssignmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.AssignmentId })
            .HasDatabaseName("ix_assignment_question_generations_tenant_assignment");
    }
}
