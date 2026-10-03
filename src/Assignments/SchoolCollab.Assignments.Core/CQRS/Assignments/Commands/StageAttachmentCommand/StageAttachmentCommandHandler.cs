using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.StageAttachmentCommand;

public sealed class StageAttachmentCommandHandler(
    IFileStore fileStore,
    IAttachmentTextExtractor extractor,
    IOptions<AttachmentUploadOptions> uploadOptions,
    ILogger<StageAttachmentCommandHandler> logger) : ICommandHandler<StageAttachmentCommand, StagedAttachmentDto>
{
    public async Task<StagedAttachmentDto> HandleAsync(
        StageAttachmentCommand command,
        CancellationToken cancellationToken = default)
    {
        StagedFileValidator.Validate(
            command.FileName, command.ContentType, command.FileSize, uploadOptions.Value);

        var storagePath = await fileStore.StoreAsync(
            command.Content, command.FileName, command.ContentType, cancellationToken);

        logger.LogInformation(
            "Staged attachment {FileName} ({FileSize} bytes) at {StoragePath}",
            command.FileName, command.FileSize, storagePath);

        // R3 / D7 (P1-4): extraction runs HERE — on stage, immediately after the store. The bytes
        // are in hand, it is outside the (already large) create transaction, and its outcome is a
        // status field on this response, so an unreadable or hostile file can never fail an upload.
        // The re-read through IFileStore is what makes "regenerate later" (D3) work from the same seam.
        AttachmentExtractionResult extraction;
        try
        {
            await using var stored = await fileStore.OpenReadAsync(storagePath, cancellationToken);
            extraction = await extractor.ExtractAsync(
                stored, command.FileName, command.FileSize, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // EC-2: a cancelled request is not an extraction outcome.
            throw;
        }
        catch (Exception ex)
        {
            // Defence in depth: IAttachmentTextExtractor is contract-bound to fail open, but a
            // stored file that vanished between the store and the re-read must not 500 the upload.
            logger.LogWarning(ex, "Extraction of the staged file {FileName} failed", command.FileName);
            extraction = AttachmentExtractionResult.Failed("The file could not be read after upload.");
        }

        if (extraction.Status != AttachmentExtractionStatus.Succeeded)
        {
            logger.LogInformation(
                "Attachment {FileName} extracted as {Status}: {Error}",
                command.FileName, extraction.Status, extraction.Error);
        }

        return new StagedAttachmentDto(
            command.FileName,
            command.ContentType,
            command.FileSize,
            storagePath,
            (AttachmentExtractionStatusDto)extraction.Status,
            extraction.Text,
            // An attempt happened (even a rejected one), so the timestamp is meaningful for every
            // non-NotAttempted status — the same rule the regenerate handler applies.
            DateTimeOffset.UtcNow,
            extraction.Error);
    }
}
