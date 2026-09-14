using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

/// <summary>
/// Strict tenant entity (WS-C1). No relationship declarations and no
/// aggregate-side navigation — the signature event is a standalone audit row
/// referenced by ids only (the <see cref="SubmissionReviewConfiguration"/> /
/// <see cref="GuardianSubmissionGateConfiguration"/> posture for standalone
/// referencing rows). The unique (AssignmentId, StudentId) index is the
/// one-successful-sign-per-pair DB backstop; the already-signed state check in
/// the sign handler is the first guard (spec §6 NFR line 115).
/// </summary>
internal sealed class SignatureEventConfiguration : TenantEntityTypeConfigurationBase<SignatureEvent>
{
    public SignatureEventConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<SignatureEvent> builder)
    {
        builder.ToTable("signature_events");

        builder.ConfigureAuditProperties();

        builder.Property(x => x.AssignmentId).IsRequired();
        builder.Property(x => x.StudentId).IsRequired();
        builder.Property(x => x.SignerGuardianId).IsRequired();
        builder.Property(x => x.SignatureType).IsRequired();
        builder.Property(x => x.TypedSignature).HasMaxLength(200);
        builder.Property(x => x.IpAddress).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UserAgent).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ConsentTextShown).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.CertificateStoragePath).HasMaxLength(500);

        // One successful sign per (assignment, student) pair ever (spec §6 NFR
        // line 115 — idempotent signature handling; the DB backstop behind the
        // already-signed state check).
        builder.HasIndex(x => new { x.AssignmentId, x.StudentId })
            .IsUnique()
            .HasDatabaseName("ix_signature_events_assignment_student");
    }
}
