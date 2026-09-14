using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.SignatureConsentTexts.Queries.GetTenantSignatureConsentText;

/// <summary>
/// Returns the current tenant's guardian sign-off consent-text override, or
/// <see langword="null"/> when none has been configured yet (a null result means
/// the caller falls back to the embedded default — WS-C2 / spec §3.2 line 53).
/// </summary>
public sealed record GetTenantSignatureConsentText : IQuery<TenantSignatureConsentTextDto?>
{
    public static readonly GetTenantSignatureConsentText Instance = new();
}
