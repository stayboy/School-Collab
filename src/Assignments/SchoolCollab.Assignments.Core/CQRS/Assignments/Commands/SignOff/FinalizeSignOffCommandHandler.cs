using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SignOff;

/// <summary>
/// WS-C1/C4 + C3 — finalizes a signed sign-off (teacher action; spec §3.2
/// line 51). The domain method enforces the Signed-only guard and stamps
/// <c>FinalizedAt</c> (terminal locked marker — further submissions stay
/// blocked). C3 extends the flow: after finalize stamps, the handler builds the
/// certificate content (assignment title + signer/student names + audit fields
/// from the signature event), generates the PDF via
/// <see cref="IAssignmentCertificateGenerator"/>, stores it via
/// <see cref="IFileStore"/> and attaches the opaque storage path on the event
/// via <see cref="SignatureEvent.AttachCertificate"/>.
///
/// <para>Transactional: a single <see cref="ISubmissionRepository.SaveChangesAsync"/>
/// runs LAST, so a generation/store failure (typed
/// <see cref="AssignmentCertificateException"/>) throws before any persistence and the
/// finalize is cleanly retryable by finalizing again — the in-memory mutation is never
/// committed.</para>
/// </summary>
public sealed class FinalizeSignOffCommandHandler(
    ISubmissionRepository submissionRepository,
    IAssignmentRepository assignmentRepository,
    IStudentDirectory studentDirectory,
    ISignatureConsentTextResolver consentResolver,
    IAssignmentCertificateGenerator certificateGenerator,
    IFileStore fileStore,
    ILogger<FinalizeSignOffCommandHandler> logger) : ICommandHandler<FinalizeSignOffCommand>
{
    public async Task HandleAsync(FinalizeSignOffCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Finalizing sign-off for assignment {AssignmentId} / student {StudentId}",
            command.AssignmentId, command.StudentId);

        var submission = await submissionRepository.GetSubmissionByAssignmentStudentAsync(
            command.AssignmentId, command.StudentId, cancellationToken)
            ?? throw new SubmissionNotFoundException(command.StudentId);

        // Finalize requires a prior sign — the audit event is the source of the
        // signer/typed-name/consent fields below.
        var signatureEvent = await submissionRepository.GetSignatureEventByAssignmentStudentAsync(
            command.AssignmentId, command.StudentId, cancellationToken)
            ?? throw new SubmissionSignOffStateException(
                "A sign-off can only be finalized after the guardian has signed.");

        var assignment = await assignmentRepository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        submission.FinalizeSignOff();

        // Names degrade to the raw id on a directory miss (the
        // GetSignOffContextQueryHandler precedent).
        var studentName = (await studentDirectory.GetStudentNameAsync(command.StudentId, cancellationToken))
            ?.DisplayName ?? command.StudentId.ToString();

        var signerName = signatureEvent.SignerGuardianId.ToString();
        var guardians = await studentDirectory.GetGuardiansAsync(command.StudentId, cancellationToken);
        var signer = guardians.FirstOrDefault(g => g.GuardianId == signatureEvent.SignerGuardianId);
        if (signer is not null)
        {
            signerName = signer.DisplayName;
        }

        // The consent shown at signing is captured verbatim on the audit event
        // (the authoritative source for the certificate); the resolver is the
        // fail-safe fallback if the event ever lacks a recorded consent.
        var consentText = string.IsNullOrWhiteSpace(signatureEvent.ConsentTextShown)
            ? await consentResolver.ResolveConsentTextAsync(cancellationToken)
            : signatureEvent.ConsentTextShown;

        var content = new AssignmentCertificateContent(
            assignment.Title,
            studentName,
            signerName,
            signatureEvent.SignatureType,
            signatureEvent.TypedSignature,
            signatureEvent.SignedAt,
            submission.FinalizedAt ?? DateTimeOffset.UtcNow,
            consentText);

        byte[] pdf;
        try
        {
            pdf = await certificateGenerator.GenerateAsync(content, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AssignmentCertificateException(
                $"Failed to generate the certificate for assignment {command.AssignmentId} / student {command.StudentId}.", ex);
        }

        string storagePath;
        try
        {
            using var stream = new MemoryStream(pdf);
            storagePath = await fileStore.StoreAsync(stream, "certificate.pdf", "application/pdf", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AssignmentCertificateException(
                $"Failed to store the certificate for assignment {command.AssignmentId} / student {command.StudentId}.", ex);
        }

        signatureEvent.AttachCertificate(storagePath);

        submissionRepository.Update(submission);
        await submissionRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Finalized sign-off for student {StudentId} / assignment {AssignmentId} with certificate {StoragePath}",
            command.StudentId, command.AssignmentId, storagePath);
    }
}
