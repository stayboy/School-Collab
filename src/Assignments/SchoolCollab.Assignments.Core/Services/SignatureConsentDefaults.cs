namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// WS-C2 / spec §3.2 line 53 — the embedded default consent language shown when
/// a tenant has not configured <c>TenantSignatureConsentText</c> (Settings) or
/// the settings fetch fails (fail-open — signing is never blocked by a Settings
/// outage). The Settings row carries only the override; this constant is the
/// fallback the consent resolver always guarantees.
/// </summary>
public static class SignatureConsentDefaults
{
    /// <summary>The default guardian sign-off consent language (sober school-context wording; G2 legal review later).</summary>
    public const string EmbeddedConsentText =
        "By signing below I confirm I have reviewed my ward's completed work for this assignment and consent to it being recorded and finalized.";
}
