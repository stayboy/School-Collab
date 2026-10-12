namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Attachment upload configuration (WS-A1 / FR-210 / EC-4). Bound in
/// <c>AddAssignmentsCore</c> from the <see cref="SectionName"/> section.
/// Per-file and total size caps are enforced by
/// <c>StagedFileValidator</c> + <c>AssignmentContentValidator</c>;
/// the staging interval / retention knobs drive
/// <c>StagedFileSweepService</c>.
/// </summary>
public sealed class AttachmentUploadOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Assignments:AttachmentUpload";

    /// <summary>Per-file size cap enforced at stage (bytes).
    /// Default 25 MiB — under Kestrel's default 30 MB request-body
    /// limit; raising this above ~30 MB also requires raising the
    /// Kestrel <c>MaxRequestBodySize</c>.</summary>
    public long MaxFileSizeBytes { get; set; } = 26214400;

    /// <summary>Total attachment size cap enforced on create/update
    /// (bytes). Default 100 MiB.</summary>
    public long MaxTotalSizeBytes { get; set; } = 104857600;

    /// <summary>Allowed file extensions (case-insensitive). Default
    /// mirrors the WS-A1 spec allowlist (PDF/DOC/DOCX/PPT/PPTX/XLS/XLSX
    /// + common image + text formats).</summary>
    public string[] AllowedExtensions { get; set; } =
    [
        ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx",
        ".png", ".jpg", ".jpeg", ".gif", ".webp",
        ".txt", ".md", ".csv",
        // QR-5 / round Q1 (owner, 2026-10-09): the media kinds a question response or an instruction
        // can carry. The list was document/image-only while it existed for AI grounding alone; the
        // response definitions make audio and video first-class, so the same list — the one the
        // server's StagedFileValidator and the client policy both read — carries them.
        ".mp3", ".m4a", ".wav", ".ogg", ".aac",
        ".mp4", ".webm", ".mov", ".m4v"
    ];

    /// <summary>How long staged files live before the sweep is
    /// allowed to delete them (hours). Default 48 h.</summary>
    public int StagingRetentionHours { get; set; } = 48;

    /// <summary>How often the orphan sweep runs (hours). Default 24 h.</summary>
    public int SweepIntervalHours { get; set; } = 24;
}
