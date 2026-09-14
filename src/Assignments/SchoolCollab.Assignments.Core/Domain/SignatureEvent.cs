using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// WS-C1 / spec §5 line 96 — the append-only guardian signature audit row
/// (the DropSign full trail: who signed, when, IP/device, consent language
/// shown at signing — spec §3.2 line 53 / §6 auditability line 116).
/// Write-once-on-sign: NO update methods, NO soft delete, NO row version (the
/// <see cref="AssignmentResource"/> write-once posture; immutability is the
/// design — G2 audit review). One event per (AssignmentId, StudentId) pair ever:
/// the sign handler's already-signed check is the first guard and the
/// <c>ix_signature_events_assignment_student</c> unique index the DB backstop.
/// </summary>
public sealed class SignatureEvent : ITenantEntity, IEntity, IAuditableEntity
{
    private SignatureEvent() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }

    public Guid AssignmentId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid SignerGuardianId { get; private set; }
    public SignatureType SignatureType { get; private set; }

    /// <summary>The typed full name for <see cref="SignatureType.Typed"/> signatures; <see langword="null"/> for Click.</summary>
    public string? TypedSignature { get; private set; }

    /// <summary>UTC moment the guardian signed (stamped at creation; mirrors the submission's SignedAt).</summary>
    public DateTimeOffset SignedAt { get; private set; }

    /// <summary>The caller IP captured at the endpoint (spec §3.2 line 53 — IP/device audit).</summary>
    public string IpAddress { get; private set; } = default!;

    /// <summary>The caller user-agent captured at the endpoint (device audit).</summary>
    public string UserAgent { get; private set; } = default!;

    /// <summary>
    /// The consent language presented at signing, stored verbatim (not a hash —
    /// the audit must show what was presented; G2 legal/retention review later).
    /// </summary>
    public string ConsentTextShown { get; private set; } = default!;

    /// <summary>
    /// C3 certificate reference — populated by the later certificate round;
    /// nothing reads it in this round (WS-C decision header).
    /// </summary>
    public string? CertificateStoragePath { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Append-only factory — the ONLY way a signature event row is created.
    /// Rejects empty audit fields (typed <see cref="ArgumentException"/>) and a
    /// Typed signature without the signer's full name.
    /// </summary>
    public static SignatureEvent Create(
        Guid tenantId,
        Guid assignmentId,
        Guid studentId,
        Guid signerGuardianId,
        SignatureType signatureType,
        string? typedSignature,
        string ipAddress,
        string userAgent,
        string consentTextShown)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (assignmentId == Guid.Empty)
            throw new ArgumentException("Assignment id is required.", nameof(assignmentId));
        if (studentId == Guid.Empty)
            throw new ArgumentException("Student id is required.", nameof(studentId));
        if (signerGuardianId == Guid.Empty)
            throw new ArgumentException("Signer guardian id is required.", nameof(signerGuardianId));

        var trimmedTypedSignature = typedSignature?.Trim();
        if (signatureType == SignatureType.Typed && string.IsNullOrWhiteSpace(trimmedTypedSignature))
            throw new ArgumentException("A typed signature requires the signer's full name.", nameof(typedSignature));

        var trimmedIpAddress = ipAddress?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedIpAddress))
            throw new ArgumentException("The signer IP address is required for the audit trail.", nameof(ipAddress));

        var trimmedUserAgent = userAgent?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedUserAgent))
            throw new ArgumentException("The signer user-agent is required for the audit trail.", nameof(userAgent));

        var trimmedConsent = consentTextShown?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedConsent))
            throw new ArgumentException("The consent language shown at signing is required.", nameof(consentTextShown));

        var now = DateTimeOffset.UtcNow;
        return new SignatureEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AssignmentId = assignmentId,
            StudentId = studentId,
            SignerGuardianId = signerGuardianId,
            SignatureType = signatureType,
            TypedSignature = signatureType == SignatureType.Typed ? trimmedTypedSignature : null,
            SignedAt = now,
            IpAddress = trimmedIpAddress,
            UserAgent = trimmedUserAgent,
            ConsentTextShown = trimmedConsent,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
