using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.StageAttachmentCommand;

/// <summary>Pure validation for inbound <see cref="StageAttachmentCommand"/>
/// payloads (WS-A1 / FR-210). Mirrors the typed-exception pattern from
/// <c>QuestionOptionDtoValidator</c> — <see cref="StagedFileRejectedException"/>
/// only, with <see cref="StagedFileRejectedException.IsSizeLimit"/>
/// distinguishing the size cap (mapped to <c>413</c>) from the rest
/// (mapped to <c>400</c>). Validation runs BEFORE any I/O so the
/// <see cref="IFileStore"/> receives zero calls on a rejected payload.</summary>
internal static class StagedFileValidator
{
    public static void Validate(
        string fileName,
        string contentType,
        long fileSize,
        AttachmentUploadOptions options)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetExtension(fileName).Length == 0)
        {
            throw new StagedFileRejectedException(
                "A file name with an extension is required.");
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new StagedFileRejectedException(
                "The file content type is missing.");
        }

        var extension = Path.GetExtension(fileName);
        if (Array.IndexOf(options.AllowedExtensions, extension) < 0
            && Array.IndexOf(options.AllowedExtensions, extension.ToLowerInvariant()) < 0)
        {
            throw new StagedFileRejectedException(
                $"'{extension}' files are not allowed.");
        }

        if (fileSize <= 0)
        {
            throw new StagedFileRejectedException(
                "The file is empty.");
        }

        if (fileSize > options.MaxFileSizeBytes)
        {
            throw new StagedFileRejectedException(
                $"The file exceeds the {options.MaxFileSizeBytes}-byte per-file limit.",
                isSizeLimit: true);
        }
    }
}
