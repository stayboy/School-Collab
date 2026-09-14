namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Optional tenant-level override of the guardian sign-off consent language
/// (assignment-request-implementation-details.md §2 WS-C; spec §3.2 line 53).
/// The resolver ALWAYS returns a usable consent string — the tenant row's text
/// when configured, else <see cref="SignatureConsentDefaults.EmbeddedConsentText"/>.
/// Interface lives in Assignments.Core; the HTTP implementation
/// (settings-api named client) lives in Assignments.Api. Mirrors
/// <see cref="ISignatureDefaultResolver"/>.
/// </summary>
public interface ISignatureConsentTextResolver
{
    /// <summary>Resolves the consent language to present at signing — never
    /// null, never empty. Fail-open: any settings-api failure (or an unset
    /// tenant row) falls back to the embedded default so signing is never
    /// blocked by a Settings outage (the signature resolver's posture).</summary>
    Task<string> ResolveConsentTextAsync(CancellationToken cancellationToken = default);
}