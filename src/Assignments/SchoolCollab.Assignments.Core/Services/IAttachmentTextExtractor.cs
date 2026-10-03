namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Outcome of one attachment text-extraction attempt (R3 / D4). Persisted on
/// <c>AssignmentAttachment.ExtractionStatus</c> and surfaced on the stage response so the
/// upload never fails because of extraction (D7 fail-open).
/// <para><see cref="NotAttempted"/> is the value every pre-R3 row and every non-persisted
/// stage carries; the other three are terminal for one attempt.</para>
/// </summary>
public enum AttachmentExtractionStatus
{
    /// <summary>No extraction has ever run for this attachment.</summary>
    NotAttempted = 0,

    /// <summary>Text was extracted; <c>ExtractedText</c> carries it (possibly capped).</summary>
    Succeeded = 1,

    /// <summary>The format is supported but the read failed, timed out, or the bytes did not
    /// match the declared format's signature. <c>ExtractionError</c> explains why.</summary>
    Failed = 2,

    /// <summary>The format is out of R3's extraction scope (D2: PDF + DOCX only — no OCR,
    /// no images, no legacy binary Office formats) or the file exceeded the extraction-specific
    /// cap and was never parsed.</summary>
    Unsupported = 3
}

/// <summary>
/// The result of one extraction attempt. <see cref="ScannedCharacters"/> is instrumentation for
/// D8(b) and is deliberately <b>not</b> persisted: it counts every character the reader observed
/// (retained <i>and</i> dropped), so a test can assert the reader stopped <em>at</em> the ceiling
/// instead of materialising a decompression bomb and then truncating.
/// </summary>
public sealed record AttachmentExtractionResult(
    AttachmentExtractionStatus Status,
    string? Text,
    string? Error,
    int ScannedCharacters)
{
    /// <summary>The fail-open "we could not read this" result.</summary>
    public static AttachmentExtractionResult Failed(string error) =>
        new(AttachmentExtractionStatus.Failed, null, error, 0);

    /// <summary>The fail-open "this format is out of scope / too large to parse" result.</summary>
    public static AttachmentExtractionResult Unsupported(string error) =>
        new(AttachmentExtractionStatus.Unsupported, null, error, 0);

    /// <summary>A successful read of <paramref name="text"/>.</summary>
    public static AttachmentExtractionResult Succeeded(string text, int scannedCharacters) =>
        new(AttachmentExtractionStatus.Succeeded, text, null, scannedCharacters);
}

/// <summary>
/// The bounds every extraction attempt is held inside (R3 / D8). Declared once, on the port, so
/// the Api implementation and the tests that pin the behaviour cannot drift.
/// </summary>
public static class AttachmentExtractionLimits
{
    /// <summary>Extraction-only input cap (bytes). Deliberately <b>below</b>
    /// <c>AttachmentUploadOptions.MaxFileSizeBytes</c> (25 MiB, the stage refusal): a file this
    /// large is still accepted and stored, but is reported <see cref="AttachmentExtractionStatus.Unsupported"/>
    /// <b>without ever being parsed</b> — a distinct, softer ceiling than the stage's 413.</summary>
    public const long MaxInputBytes = 10 * 1024 * 1024; // 10 MiB

    /// <summary>Character ceiling for a retained extraction (D8(a)+(b)). The reader stops
    /// <b>at</b> this count while streaming; it never materialises the whole document and then
    /// truncates. Equal to the AI host's <c>MaxResourceTextLength</c> so one attachment's text can
    /// ground a generation whole.</summary>
    public const int MaxCharacters = 20000;

    /// <summary>Bounded <c>ExtractionError</c> column cap (D8(c)).</summary>
    public const int MaxErrorLength = 500;

    /// <summary>Per-file wall-clock budget (D8(a)) — the linked token the extractor cancels.</summary>
    public const int TimeoutSeconds = 15;
}

/// <summary>
/// Server-side port for reading the plain text out of a staged upload (R3 / D1/D2). The
/// <b>Assignments</b> host owns and runs this because it owns the staged bytes
/// (<see cref="IFileStore"/>, <c>StageAttachmentSweepService</c>, <c>StageAttachmentCommand</c>) and
/// the AI host is verifiably file-blind — the text reaches it only through the existing
/// <c>QuestionGenerationRequest.ResourceTexts</c> seam (AI-3).
/// <para><b>Contract:</b> this never throws for a content problem. Every format, signature,
/// timeout, size and parser failure is reported as a non-<see cref="AttachmentExtractionStatus.Succeeded"/>
/// result, because extraction runs inside the stage request and must never fail an upload (D7).
/// A caller-cancelled <paramref name="cancellationToken"/> still propagates.</para>
/// </summary>
public interface IAttachmentTextExtractor
{
    /// <summary>
    /// Reads up to <see cref="AttachmentExtractionLimits.MaxCharacters"/> characters of text from
    /// <paramref name="content"/>. <paramref name="fileName"/> selects the format (the extension is
    /// the only validated hint today — <c>StagedFileValidator</c> never checks
    /// <c>ContentType</c>), <paramref name="fileSize"/> drives the input cap, and the real header is
    /// sniffed so a mis-declared extension fails closed (D8(d)).
    /// </summary>
    Task<AttachmentExtractionResult> ExtractAsync(
        Stream content,
        string fileName,
        long fileSize,
        CancellationToken cancellationToken = default);
}
