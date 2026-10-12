using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Application.Helpers;

/// <summary>Where a file dropped on the materials section goes (D5, revised 2026-10-10).</summary>
public enum MaterialDropDestination
{
    /// <summary>Audio, video or image — becomes an instruction row (like the media kinds).</summary>
    InstructionMedia,

    /// <summary>A document — becomes a resource/attachment (the AI reference set).</summary>
    Resource,

    /// <summary>Neither — refused by name; never accepted and silently dropped (D5).</summary>
    Refused,
}

/// <summary>
/// D5 (revised, redesign round): one dropzone, two destinations, inferred from the file.
///
/// <para>Before this round a dropped document was refused outright and the author was pointed at the
/// resources tile. The section now owns the accept-list as well as the media kinds, so the dropzone
/// routes instead of refusing: media → an instruction row; anything the upload policy accepts →
/// a resource; only a file in neither set is refused, by name.</para>
///
/// <para>Pure and public on purpose: AC-10 is a unit test here, and the page keeps a one-line
/// switch on the result.</para>
/// </summary>
public static class MaterialDropRouting
{
    /// <summary>The destination for a dropped file, in precedence order: media kind, then an
    /// accepted upload (resource), then refusal.</summary>
    public static MaterialDropDestination ForDrop(string fileName, string? contentType, long fileSize)
    {
        if (InstructionKindInference.ForFile(fileName, contentType) is not null)
        {
            return MaterialDropDestination.InstructionMedia;
        }

        // The policy is the section's accept-list (it mirrors the server's); null means "accepted".
        return AttachmentUploadPolicy.Validate(fileName, fileSize) is null
            ? MaterialDropDestination.Resource
            : MaterialDropDestination.Refused;
    }

    /// <summary>The named reason a dropped file the section cannot take is refused (AC-10). It names
    /// the menu item that would accept it, in the same Title Case the menu uses.</summary>
    public static string NotAcceptedRefusal(string fileName) =>
        $"'{fileName}' is not a supported material — add an image, audio, video, PDF or document, or use Upload From Device.";
}
