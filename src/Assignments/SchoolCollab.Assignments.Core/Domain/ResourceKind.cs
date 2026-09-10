namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// Kind of AI-generation input on an assignment (WS-A1 / spec §3.2 /
/// FR-211). <c>Url</c> is a plain link, <c>File</c> is an uploaded file
/// staged via the file-store (opaque <c>StoragePath</c>), and <c>Video</c>
/// is either an external embed URL or an uploaded video file. The kind
/// matrix is validator-owned (see <c>AssignmentContentValidator</c>) — this
/// enum only labels the row.
/// </summary>
public enum ResourceKind
{
    Url = 0,
    File = 1,
    Video = 2
}
