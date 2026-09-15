using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// C3 — the immutable inputs the certificate renderer needs. Assembled by the
/// finalize command handler from the assignment (title), the audit signature
/// event (signer/type/typed name/signed-at/consent shown verbatim) and the
/// student directory (student + signer-guardian display names — callers
/// already degrade to the raw id on a lookup miss, the GetSignOffContext
/// precedent). Layout is a single A4 page; the record carries no layout or
/// styling — only the data the renderer projects.
/// </summary>
public sealed record AssignmentCertificateContent(
    string AssignmentTitle,
    string StudentName,
    string SignerGuardianName,
    SignatureType SignatureType,
    string? TypedSignature,
    DateTimeOffset SignedAt,
    DateTimeOffset FinalizedAt,
    string ConsentTextShown);
