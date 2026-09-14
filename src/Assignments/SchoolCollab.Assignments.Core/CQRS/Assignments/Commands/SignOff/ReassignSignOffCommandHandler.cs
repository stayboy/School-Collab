using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SignOff;

/// <summary>
/// WS-C1 (Q5 delegation) — reassigns the expected signer of a sign-off (spec
/// §3.2). The new guardian must be a linked guardian of the ward (fail-CLOSED
/// via <see cref="IStudentDirectory"/>); the domain method enforces the
/// AwaitingSignature-only state guard.
/// </summary>
public sealed class ReassignSignOffCommandHandler(
    ISubmissionRepository submissionRepository,
    IStudentDirectory studentDirectory,
    ILogger<ReassignSignOffCommandHandler> logger) : ICommandHandler<ReassignSignOffCommand>
{
    public async Task HandleAsync(ReassignSignOffCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Reassigning signer for assignment {AssignmentId} / student {StudentId} to guardian {GuardianId}",
            command.AssignmentId, command.StudentId, command.NewGuardianId);

        if (command.NewGuardianId == Guid.Empty)
            throw new ArgumentException("New guardian id is required.", nameof(command.NewGuardianId));

        var submission = await submissionRepository.GetSubmissionByAssignmentStudentAsync(
            command.AssignmentId, command.StudentId, cancellationToken)
            ?? throw new SubmissionNotFoundException(command.StudentId);

        // Fail-CLOSED: the assignee must be a linked guardian of the ward.
        var isGuardian = await studentDirectory.IsGuardianOfAsync(command.StudentId, command.NewGuardianId, cancellationToken);
        if (!isGuardian)
        {
            throw new GuardianNotAuthorizedException(command.StudentId, command.NewGuardianId);
        }

        // Domain state guard (AwaitingSignature-only) inside.
        submission.ReassignSigner(command.NewGuardianId);
        submissionRepository.Update(submission);
        await submissionRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Reassigned signer for student {StudentId} / assignment {AssignmentId} to guardian {GuardianId}",
            command.StudentId, command.AssignmentId, command.NewGuardianId);
    }
}
