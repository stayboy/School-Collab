using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Application.Helpers;

/// <summary>
/// D4/D5 (spec <c>instructional-materials</c>, owner 2026-10-10): the section dropzone's kind
/// inference. A dropped file's kind is read from its declared content type first and its extension
/// second — the declared type alone is not trustworthy (a browser may send
/// <c>application/octet-stream</c> for a perfectly good audio file, and some sends lie outright),
/// and the extension alone misses the type the upload actually carries.
///
/// <para><b>Refusal is a first-class outcome (D5).</b> A document has no instruction kind that can
/// represent it (the enums are not widened this round — D13), so it is refused with a named reason
/// that points at the file path rather than accepted and silently dropped. <c>null</c> therefore
/// means "refuse", never "unknown, guess later".</para>
///
/// <para>Pure and public on purpose: AC-3's inference and AC-5's refusal are unit-testable here
/// instead of only through a rendered component.</para>
/// </summary>
public static class InstructionKindInference
{
    /// <summary>The instruction kind a dropped file maps to, or <c>null</c> when no kind can
    /// represent it — the caller refuses with <see cref="DocumentRefusal"/>.</summary>
    public static InstructionKindDto? ForFile(string fileName, string? contentType) =>
        FromContentType(contentType) ?? FromExtension(Path.GetExtension(fileName));

    /// <summary>The named reason a dropped document is refused by a MEDIA ROW (AC-3): it names the
    /// file-material path the author should use instead, so the refusal is actionable rather than
    /// silent. The section-level dropzone routes documents to resources instead (D5 revised) — this
    /// path remains for the per-row replace control, which holds one media file.</summary>
    public static string DocumentRefusal(string fileName) =>
        $"'{fileName}' is a document — use Upload From Device, or drop it on the Content & Resources dropzone.";

    private static InstructionKindDto? FromContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var type = contentType.Trim();
        if (type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return InstructionKindDto.Audio;
        }

        if (type.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return InstructionKindDto.Video;
        }

        return type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? InstructionKindDto.Image : null;
    }

    private static InstructionKindDto? FromExtension(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".mp3" or ".m4a" or ".wav" or ".ogg" or ".aac" => InstructionKindDto.Audio,
            ".mp4" or ".webm" or ".mov" or ".m4v" => InstructionKindDto.Video,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" => InstructionKindDto.Image,
            _ => null,
        };
}
