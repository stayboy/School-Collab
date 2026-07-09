using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (global-tenant-filter.md §3.2). Inherits the subject's
/// tenant.
/// </summary>
internal sealed class SubjectLessonConfiguration : TenantEntityTypeConfigurationBase<SubjectLesson>
{
    public SubjectLessonConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<SubjectLesson> builder)
    {
        builder.ToTable("subject_lessons");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.DisplayOrder).IsRequired();
        builder.Property(x => x.SubjectId).IsRequired();

        builder.HasOne(x => x.Subject)
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Strand)
            .WithMany()
            .HasForeignKey(x => x.StrandId)
            .OnDelete(DeleteBehavior.SetNull);

        // NFR-3 hot paths (tenant_id leading).
        builder.HasIndex(x => new { x.TenantId, x.SubjectId })
            .HasDatabaseName("ix_subject_lessons_tenant_subject");

        builder.HasIndex(x => x.StrandId)
            .HasDatabaseName("ix_subject_lessons_strand");

        builder.Ignore(x => x.DomainEvents);
    }
}
