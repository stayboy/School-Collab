using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// One uploaded resource file on an assignment (WS-A1 / FR-210–212). R3 (D4) adds the
/// persisted text-extraction outcome.
/// <para><b>These rows are re-minted on every save</b> — <c>Assignment.AddAttachment</c> creates a
/// fresh instance and the create/update handlers full-replace the owned collection — so the
/// extraction outcome survives only because it rides the wire DTOs
/// (<c>StagedAttachmentDto</c> on stage → <c>NewAttachmentDto</c> on save → here), exactly as
/// <see cref="StoragePath"/> already does. A field added here without that round-trip is wiped by
/// the author's next save.</para>
/// </summary>
public sealed class AssignmentAttachment
{
    private AssignmentAttachment() { }

    internal AssignmentAttachment(
        Guid assignmentId,
        string fileName,
        string contentType,
        long fileSize,
        string storagePath,
        AttachmentExtractionStatus extractionStatus = AttachmentExtractionStatus.NotAttempted,
        string? extractedText = null,
        DateTimeOffset? extractedAt = null,
        string? extractionError = null)
    {
        Id = Guid.NewGuid();
        AssignmentId = assignmentId;
        FileName = fileName;
        ContentType = contentType;
        FileSize = fileSize;
        StoragePath = storagePath;
        // Normalise here rather than trusting the wire: the extraction outcome round-trips through
        // the browser, so a hand-edited payload must not be able to leave the row in a state that
        // contradicts itself (text with a Failed status, or an error on a Succeeded one).
        ExtractionStatus = extractionStatus;
        ExtractedText = extractionStatus == AttachmentExtractionStatus.Succeeded ? extractedText : null;
        ExtractedAt = extractedAt;
        ExtractionError = extractionStatus == AttachmentExtractionStatus.Succeeded ? null : extractionError;
    }

    public Guid Id { get; private set; }
    public Guid AssignmentId { get; private set; }
    public string FileName { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long FileSize { get; private set; }
    public string StoragePath { get; private set; } = default!;

    /// <summary>R3 / D4: whether text was ever extracted from this attachment's staged bytes.
    /// <see cref="ExtractionStatus"/> is <see cref="AttachmentExtractionStatus.NotAttempted"/> for
    /// every pre-R3 row and for an attachment that was staged before extraction existed.</summary>
    public AttachmentExtractionStatus ExtractionStatus { get; private set; } = AttachmentExtractionStatus.NotAttempted;

    /// <summary>R3 / D4: the extracted text, capped at
    /// <see cref="AttachmentExtractionLimits.MaxCharacters"/> by the extractor. Null unless
    /// <see cref="ExtractionStatus"/> is <see cref="AttachmentExtractionStatus.Succeeded"/>.</summary>
    public string? ExtractedText { get; private set; }

    /// <summary>R3 / D4: when the current <see cref="ExtractionStatus"/> was recorded.</summary>
    public DateTimeOffset? ExtractedAt { get; private set; }

    /// <summary>R3 / D4/D8(c): why a non-<see cref="AttachmentExtractionStatus.Succeeded"/>
    /// outcome happened, bounded to <see cref="AttachmentExtractionLimits.MaxErrorLength"/>. This is
    /// what the author is shown so an unreadable upload is surfaced, never silently dropped.</summary>
    public string? ExtractionError { get; private set; }

    /// <summary>R3 / D4/D5: records one extraction attempt's outcome. A
    /// <see cref="AttachmentExtractionStatus.Succeeded"/> requires text; any other status clears
    /// it, so the persisted pair can never contradict itself. This is the <b>only</b> writer after
    /// construction, and it is the regenerate path's write point (criterion 5).</summary>
    internal void ApplyExtraction(
        AttachmentExtractionStatus status,
        string? text,
        string? error,
        DateTimeOffset? extractedAt)
    {
        ExtractionStatus = status;
        ExtractedText = status == AttachmentExtractionStatus.Succeeded ? text : null;
        ExtractionError = status == AttachmentExtractionStatus.Succeeded ? null : error;
        ExtractedAt = extractedAt;
    }
}
