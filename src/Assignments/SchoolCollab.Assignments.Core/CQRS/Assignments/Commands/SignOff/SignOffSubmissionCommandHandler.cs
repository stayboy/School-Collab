using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.SignOff;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SignOff;

/// <summary>
/// WS-C2 sign-off command handler (spec §3.2 line 50 + §6 NFR line 115). Order
/// is binding: typed validation → assignment (404 + RequiresSignature guard) →
/// submission (404) → already-signed check (409, BEFORE any write) → guardian
/// link check (fail-CLOSED) → consent resolution → event + MarkSigned in ONE
/// atomic save. Returns the refreshed per-ward sign-off status row.
/// </summary>
public sealed class SignOffSubmissionCommandHandler(
    IAssignmentRepository assignmentRepository,
    ISubmissionRepository submissionRepository,
    ITenantProvider tenantProvider,
    IStudentDirectory studentDirectory,
    ISignatureConsentTextResolver consentResolver,
    ILogger<SignOffSubmissionCommandHandler> logger) : ICommandHandler<SignOffSubmissionCommand, SignOffStatusDto>
{
    public async Task<SignOffStatusDto> HandleAsync(
        SignOffSubmissionCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Signing off assignment {AssignmentId} / student {StudentId}",
            command.AssignmentId, command.StudentId);

        // ── Typed validation ──
        if (command.AssignmentId == Guid.Empty)
            throw new ArgumentException("Assignment id is required.", nameof(command.AssignmentId));
        if (command.StudentId == Guid.Empty)
            throw new ArgumentException("Student id is required.", nameof(command.StudentId));
        if (command.GuardianId == Guid.Empty)
            throw new ArgumentException("Guardian id is required.", nameof(command.GuardianId));
        if (command.SignatureType == SignatureType.Typed && string.IsNullOrWhiteSpace(command.TypedSignature))
            throw new ArgumentException("A typed signature requires the signer's full name.", nameof(command.TypedSignature));

        // ── Assignment (404 + RequiresSignature guard) ──
        var assignment = await assignmentRepository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);
        if (!assignment.RequiresSignature)
        {
            throw new SubmissionSignOffStateException(
                "This assignment does not require a guardian signature.");
        }

        // ── Submission (404) ──
        var submission = await submissionRepository.GetSubmissionByAssignmentStudentAsync(
            command.AssignmentId, command.StudentId, cancellationToken)
            ?? throw new SubmissionNotFoundException(command.StudentId);

        // ── Already-signed (409) BEFORE any write — no second event can ever exist ──
        if (submission.SignOffState == SignOffState.Signed)
        {
            throw new SubmissionAlreadySignedException(command.StudentId);
        }
        if (submission.SignOffState != SignOffState.AwaitingSignature)
        {
            throw new SubmissionSignOffStateException(
                $"A guardian signature can only be recorded from sign-off state AwaitingSignature, was {submission.SignOffState}.");
        }

        // ── Guardian link (fail-CLOSED — a directory outage blocks signing) ──
        var isGuardian = await studentDirectory.IsGuardianOfAsync(command.StudentId, command.GuardianId, cancellationToken);
        if (!isGuardian)
        {
            throw new GuardianNotAuthorizedException(command.StudentId, command.GuardianId);
        }

        // ── Consent resolution (fail-open — never blocks signing) ──
        var consentText = await consentResolver.ResolveConsentTextAsync(cancellationToken);

        // ── Event + MarkSigned in ONE atomic save ──
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var signatureEvent = SignatureEvent.Create(
            tenantId,
            command.AssignmentId,
            command.StudentId,
            command.GuardianId,
            command.SignatureType,
            command.TypedSignature,
            command.IpAddress,
            command.UserAgent,
            consentText);

        submissionRepository.Add(signatureEvent);
        submission.MarkSigned();
        submissionRepository.Update(submission);
        await submissionRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Guardian {GuardianId} signed off student {StudentId} for assignment {AssignmentId} ({SignatureType})",
            command.GuardianId, command.StudentId, command.AssignmentId, command.SignatureType);

        // ── Refreshed per-ward status row (reuse the shared projection) ──
        var statuses = await SignOffStatusProjection.BuildAsync(
            command.AssignmentId, assignmentRepository, submissionRepository, studentDirectory, logger, cancellationToken);

        return statuses.First(r => r.StudentId == command.StudentId);
    }
}
