namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// WS-C1 / spec §3.2 line 51 — the guardian sign-off stage for a
/// per-(assignment, ward) submission. The FULL DropSign status chain
/// (Delivered → Opened → In Progress → Completed → Awaiting Signature → Signed →
/// Finalized) is deliberately NOT stored — it is a projection over recipient
/// delivery, submission progress, and this stage (impl-details §2 WS-C; do NOT
/// add a chain enum).
/// </summary>
public enum SignOffState
{
    /// <summary>No sign-off stage in effect (assignment does not require a signature, or no submission yet).</summary>
    None = 0,

    /// <summary>The ward completed the assignment; the guardian signature is awaited.</summary>
    AwaitingSignature = 1,

    /// <summary>The guardian signed (a <see cref="SignatureEvent"/> audit row exists for the pair).</summary>
    Signed = 2
}
