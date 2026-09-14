using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SignOff;

/// <summary>
/// WS-C1/C4 — finalizes a signed sign-off (teacher action; spec §3.2 line 51).
/// The domain method enforces the Signed-only guard and stamps
/// <c>FinalizedAt</c> (terminal locked marker — further submissions stay
/// blocked).
/// </summary>
public sealed class FinalizeSignOffCommandHandler(
    ISubmissionRepository submissionRepository,
    ILogger<FinalizeSignOffCommandHandler> logger) : ICommandHandler<FinalizeSignOffCommand>
{
    public async Task HandleAsync(FinalizeSignOffCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Finalizing sign-off for assignment {AssignmentId} / student {StudentId}",
            command.AssignmentId, command.StudentId);

        var submission = await submissionRepository.GetSubmissionByAssignmentStudentAsync(
            command.AssignmentId, command.StudentId, cancellationToken)
            ?? throw new SubmissionNotFoundException(command.StudentId);

        submission.FinalizeSignOff();
        submissionRepository.Update(submission);
        await submissionRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Finalized sign-off for student {StudentId} / assignment {AssignmentId}",
            command.StudentId, command.AssignmentId);
    }
}
