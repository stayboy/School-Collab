using System.Globalization;

namespace SchoolCollab.Assignments.Application.Helpers;

/// <summary>
/// Client-side convenience constants + pre-flight check for the
/// Resources UI upload control (WS-A1 / FR-210). Mirrors the server
/// defaults in
/// <c>SchoolCollab.Assignments.Core.Services.AttachmentUploadOptions</c>
/// for a friendly UX (no upload round-trip for an obviously-rejected
/// file); the server stays authoritative and re-validates every
/// payload. Not config-fed on purpose — the admin host doesn't carry
/// the assignments-api parameters, and the server-side allowlist is the
/// single source of truth.
/// </summary>
public static class AttachmentUploadPolicy
{
    /// <summary>Per-file size cap (bytes). Matches the server default.</summary>
    public const long MaxFileSizeBytes = 104857600; // 100 MiB

    /// <summary>The byte threshold at which the in-browser recorder auto-stops (recording-materials
    /// D7). Deliberately a few MiB under <see cref="MaxFileSizeBytes"/>: it halts a take before it can
    /// be lost to a server 413, while the raised server cap remains the real ceiling. A guard, not the
    /// limiter — the recorder is not bitrate-constrained, so a browser's 720p output is simply stopped
    /// here if it ever approaches the cap.</summary>
    public const long RecorderAutoStopBytes = 99614720; // 95 MiB

    /// <summary>Maximum number of files in a single selection batch.
    /// Matches the configured <c>FluentInputFile.MaximumFileCount</c> and is
    /// the source of truth for the user-facing count-cap message — the
    /// FluentUI error callback reports the attempted file count (the
    /// <c>FileCount</c> of <c>FluentInputFileErrorEventArgs</c>; the 4.14.2
    /// <c>OnFileCountExceeded</c> parameter before it), not this cap, so the
    /// message must reference this constant rather than the callback's value.</summary>
    public const int MaximumFileCount = 10;

    /// <summary>Allowed file extensions (case-insensitive). Matches the
    /// server default. Keep in lockstep with
    /// <c>AttachmentUploadOptions.AllowedExtensions</c> — including the audio/video extensions the
    /// response definitions added (round Q1, owner 2026-10-09).</summary>
    public static readonly string[] AllowedExtensions =
    [
        ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx",
        ".png", ".jpg", ".jpeg", ".gif", ".webp",
        ".txt", ".md", ".csv",
        ".mp3", ".m4a", ".wav", ".ogg", ".aac",
        ".mp4", ".webm", ".mov", ".m4v"
    ];

    /// <summary>Comma-joined extension list for the FluentUI upload
    /// control's <c>Accept</c> attribute.</summary>
    public static string AcceptExtensions => string.Join(",", AllowedExtensions);

    /// <summary>Returns null when the file passes the client pre-checks;
    /// otherwise a friendly user-facing message describing the rejection.
    /// Does NOT call the API — the server re-validates on stage.</summary>
    public static string? Validate(string fileName, long fileSize)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrEmpty(extension))
        {
            return "A file name with an extension is required.";
        }

        var matched = false;
        for (var i = 0; i < AllowedExtensions.Length; i++)
        {
            if (string.Equals(AllowedExtensions[i], extension, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
                break;
            }
        }
        if (!matched)
        {
            return $"'{extension}' files are not allowed.";
        }

        if (fileSize <= 0)
        {
            return "The file is empty.";
        }

        if (fileSize > MaxFileSizeBytes)
        {
            return $"The file exceeds the {MaxFileSizeBytes / (1024 * 1024)} MB limit.";
        }

        return null;
    }

    /// <summary>Formats a byte count as a short human-readable label
    /// ("1.2 MB", "768 KB", "4 B").</summary>
    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";
        }
        if (bytes < 1024L * 1024)
        {
            return $"{bytes / 1024.0:0.#} KB";
        }
        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024):0.#} MB";
        }
        return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
    }
}
