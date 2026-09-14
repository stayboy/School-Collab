namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// WS-C2 / spec §3.2 line 50 — the guardian e-signature method (typed, drawn,
/// or click-to-sign; drawn is recorded backlog). Typed records the signer's
/// typed full name on the <see cref="SignatureEvent"/>; Click records a
/// click-to-sign with <c>TypedSignature</c> null.
/// </summary>
public enum SignatureType
{
    /// <summary>The guardian types their full name to sign (stored on the audit event).</summary>
    Typed = 0,

    /// <summary>Click-to-sign — the audit event carries the click only (TypedSignature null).</summary>
    Click = 1
}
