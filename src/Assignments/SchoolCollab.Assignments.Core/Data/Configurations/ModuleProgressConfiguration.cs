using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

/// <summary>
/// WS-D1 (spec §3.3): strict tenant entity for per-ward module progress.
/// The <c>ContentModuleId</c> FK is declared from the
/// <c>ContentModuleConfiguration</c> side (cascade — removing a module
/// removes its progress rows); no navigation on either side
/// (single-source-of-truth declaration, the standalone-entity pattern).
/// <see cref="ModuleProgress.AssignmentId"/> is denormalized (no FK to
/// <see cref="Assignment"/>) so per-(assignment, student) lookups and the
/// unique row are tenant-scoped without crossing aggregates.
/// </summary>
internal sealed class ModuleProgressConfiguration : TenantEntityTypeConfigurationBase<ModuleProgress>
{
    public ModuleProgressConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<ModuleProgress> builder)
    {
        builder.ToTable("assignment_module_progress");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.AssignmentId).IsRequired();
        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.ContentModuleId).IsRequired();
        builder.Property(x => x.PercentComplete).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CompletedAt);

        // Per-(assignment, student, module) monotonic heartbeat row — the
        // upsert path (RecordModuleProgress) keys on this uniqueness.
        builder.HasIndex(x => new { x.TenantId, x.AssignmentId, x.StudentId, x.ContentModuleId })
            .IsUnique()
            .HasDatabaseName("uq_assignment_module_progress_tenant_assignment_student_module");
    }
}
