using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands;

/// <summary>
/// QR-5 (spec <c>question-response-types</c> §5.2/§5.6) — pure payload validation for inbound
/// <see cref="NewInstructionDto"/> rows, whichever owner they belong to (a question or the
/// assignment itself). Throws <see cref="AssignmentContentValidationException"/> with one clear
/// message before any child is added to the aggregate, so a partial state can never be persisted —
/// the <c>QuestionOptionDtoValidator</c> / <c>AssignmentContentValidator</c> precedent. Shared by the
/// create and update handlers.
///
/// <para>The kind→payload pairing is explicit on purpose: a Text row must carry text, a Url row a
/// resolvable absolute http(s) URL, and a media row the staged file's name, type and storage path
/// (the attachment staging contract — the file is already in storage by the time this runs).</para>
/// </summary>
internal static class InstructionDtoValidator
{
    /// <summary>Validate every inbound instruction for one owner; throw on the first violation.</summary>
    /// <param name="instructions">The rows to check; null means the owner carries none.</param>
    /// <param name="ownerLabel">How the owner is named in the message ("Question 2",
    /// "This assignment").</param>
    public static void ValidateAll(IReadOnlyList<NewInstructionDto>? instructions, string ownerLabel)
    {
        if (instructions is null)
        {
            return;
        }

        for (var i = 0; i < instructions.Count; i++)
        {
            var item = instructions[i];
            var where = $"{ownerLabel}: instruction at position {i}";

            if (!Enum.IsDefined(item.Kind))
            {
                throw new AssignmentContentValidationException(
                    $"{where}: unsupported instruction kind '{(int)item.Kind}'.");
            }

            switch (item.Kind)
            {
                case InstructionKindDto.Text:
                    // Round instructional-materials (D6/D7): a text material is NAMED, not bodied. The
                    // title is what makes it readable in the materials list and on the ward surface; the
                    // body is optional. The title therefore replaced the old non-blank-text requirement.
                    if (string.IsNullOrWhiteSpace(item.Title))
                    {
                        throw new AssignmentContentValidationException(
                            $"{where}: a text instruction needs its title.");
                    }

                    break;

                case InstructionKindDto.Url:
                    if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var uri)
                        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    {
                        throw new AssignmentContentValidationException(
                            $"{where}: a link instruction needs an absolute http(s) URL.");
                    }

                    break;

                default: // Audio | Video | Image — the media kinds.
                    if (string.IsNullOrWhiteSpace(item.FileName)
                        || string.IsNullOrWhiteSpace(item.ContentType)
                        || string.IsNullOrWhiteSpace(item.StoragePath))
                    {
                        throw new AssignmentContentValidationException(
                            $"{where}: a media instruction needs the staged file's name, content type "
                            + "and storage path.");
                    }

                    break;
            }
        }
    }
}
