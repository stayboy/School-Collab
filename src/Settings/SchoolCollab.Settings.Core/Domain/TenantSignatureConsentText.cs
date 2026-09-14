using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// Per-tenant guardian sign-off consent language (WS-C2 / spec §3.2 line 53 —
/// the audit trail must show the consent text presented at signing). One row per
/// tenant. <see langword="null"/> <see cref="ConsentText"/> means the caller falls
/// back to the embedded default owned by the Assignments context
/// (<c>SignatureConsentDefaults.EmbeddedConsentText</c>) — this row only carries
/// the override. Mirrors <see cref="TenantAssignmentPolicy"/> (WS-C1 prerequisite).
/// </summary>
public sealed class TenantSignatureConsentText : BaseTenantEntityWithAudit, IHasRowVersion
{
    private TenantSignatureConsentText() { }

    /// <summary>
    /// Maximum length of <see cref="ConsentText"/> in characters — bounds the
    /// verbatim copy stored on every <c>SignatureEvent</c> (spec §5 line 96 /
    /// §6 auditability).
    /// </summary>
    public const int MaxConsentTextLength = 4000;

    /// <summary>
    /// The tenant's consent-text override. <see langword="null"/> = the embedded
    /// default consent language applies.
    /// </summary>
    public string? ConsentText { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>
    /// Creates the single consent-text row for <paramref name="tenantId"/>. Callers
    /// typically use <see cref="SetConsentText"/> on an existing row instead; this
    /// factory is for the initial insert.
    /// </summary>
    public static TenantSignatureConsentText Create(Guid tenantId, string? consentText = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new TenantSignatureConsentText
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConsentText = Normalize(consentText),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Replaces the tenant consent text (pass <see langword="null"/> to restore the
    /// embedded default). Stamps <see cref="UpdatedAt"/>.
    /// </summary>
    public void SetConsentText(string? consentText)
    {
        ConsentText = Normalize(consentText);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string? Normalize(string? consentText)
    {
        var trimmed = consentText?.Trim();
        if (trimmed is { Length: > MaxConsentTextLength })
            throw new ArgumentException(
                $"Consent text must be {MaxConsentTextLength} characters or fewer.", nameof(consentText));
        return trimmed;
    }
}
