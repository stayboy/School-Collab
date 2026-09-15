namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// C3 — renders the sign-off certificate PDF for a finalized guardian
/// sign-off (assignment-request-implementation-details.md §2 WS-C; QuestPDF,
/// pin 2026.8.0). The interface lives in Core so the finalize handler can
/// depend on it; the implementation is a delivery-layer concern and lives in
/// the Assignments.Api project (the API host is the only runtime that
/// dispatches finalize). Consumers degrade the certificate content from the
/// data the handler already loads — no cross-bounded-context types here.
/// </summary>
public interface IAssignmentCertificateGenerator
{
    /// <summary>Generates a one-page A4 PDF byte array for the given finalized
    /// sign-off content. Community QuestPDF license (org &lt;$1M revenue) is set
    /// once by the implementation.</summary>
    Task<byte[]> GenerateAsync(AssignmentCertificateContent content, CancellationToken cancellationToken = default);
}
