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
    public const long MaxFileSizeBytes = 26214400; // 25 MiB

    /// <summary>Maximum number of files in a single selection batch.
    /// Matches the configured <c>FluentInputFile.MaximumFileCount</c> and is
    /// the source of truth for the user-facing count-cap message — the
    /// FluentUI <c>OnFileCountExceeded</c> callback parameter carries the
    /// attempted file count, not this cap, so the message must reference
    /// this constant rather than the callback parameter.</summary>
    public const int MaximumFileCount = 10;

    /// <summary>Allowed file extensions (case-insensitive). Matches the
    /// server default. Keep in lockstep with
    /// <c>AttachmentUploadOptions.AllowedExtensions</c>.</summary>
    public static readonly string[] AllowedExtensions =
    [
        ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx",
        ".png", ".jpg", ".jpeg", ".gif", ".webp",
        ".txt", ".md", ".csv"
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
