namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RegenerateAttachmentExtraction;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;

/// <summary>
/// R3 (D3, criterion 5) — handler for <see cref="RegenerateAttachmentExtractionCommand"/>.
/// Re-opens the attachment's blob through <see cref="IFileStore"/> and re-runs extraction,
/// <b>replacing</b> the stored outcome in place: no new attachment row is minted and no duplicate is
/// appended, so a second run converges on the same single row.
/// <para>Fail-open throughout: a blob that has since been swept, a file that is now unreadable, or a
/// parser failure all land as a terminal <see cref="AttachmentExtractionStatus.Failed"/> on the
/// <i>same</i> attachment rather than an error response, so the author sees the status transition.</para>
/// </summary>
public sealed class RegenerateAttachmentExtractionCommandHandler(
    IAssignmentRepository repository,
    IFileStore fileStore,
    IAttachmentTextExtractor extractor,
    HybridCache cache,
    ILogger<RegenerateAttachmentExtractionCommandHandler> logger)
    : ICommandHandler<RegenerateAttachmentExtractionCommand, AssignmentAttachmentReadDto>
{
    public async Task<AssignmentAttachmentReadDto> HandleAsync(
        RegenerateAttachmentExtractionCommand command,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "Regenerating extraction for attachment {AttachmentId} of assignment {AssignmentId}",
            command.AttachmentId, command.AssignmentId);

        var assignment = await repository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        var attachment = assignment.Attachments.SingleOrDefault(a => a.Id == command.AttachmentId)
            ?? throw new AttachmentNotFoundException(command.AttachmentId);

        AttachmentExtractionResult extraction;
        try
        {
            await using var content = await fileStore.OpenReadAsync(attachment.StoragePath, cancellationToken);
            extraction = await extractor.ExtractAsync(
                content, attachment.FileName, attachment.FileSize, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // EC-2: navigation cancellation is not an extraction outcome.
            throw;
        }
        catch (FileNotFoundException ex)
        {
            logger.LogWarning(ex, "The staged file for attachment {AttachmentId} is gone", command.AttachmentId);
            extraction = AttachmentExtractionResult.Failed("The uploaded file is no longer available.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Regenerating extraction for attachment {AttachmentId} failed", command.AttachmentId);
            extraction = AttachmentExtractionResult.Failed("The file could not be read.");
        }

        attachment.ApplyExtraction(extraction.Status, extraction.Text, extraction.Error, DateTimeOffset.UtcNow);

        repository.DetectChanges();
        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);
        assignment.ClearDomainEvents();

        logger.LogInformation(
            "Attachment {AttachmentId} extraction is now {Status}", command.AttachmentId, extraction.Status);

        return new AssignmentAttachmentReadDto(
            attachment.Id,
            attachment.FileName,
            attachment.ContentType,
            attachment.FileSize,
            attachment.StoragePath,
            (AttachmentExtractionStatusDto)attachment.ExtractionStatus,
            attachment.ExtractedText,
            attachment.ExtractedAt,
            attachment.ExtractionError);
    }
}
