using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.StageAttachmentCommand;

public sealed class StageAttachmentCommandHandler(
    IFileStore fileStore,
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

        return new StagedAttachmentDto(
            command.FileName, command.ContentType, command.FileSize, storagePath);
    }
}
