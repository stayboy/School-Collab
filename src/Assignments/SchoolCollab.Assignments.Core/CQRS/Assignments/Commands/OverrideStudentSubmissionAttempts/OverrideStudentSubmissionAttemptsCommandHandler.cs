using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.OverrideStudentSubmissionAttempts;

/// <summary>
/// WS-A3 (spec §7 Q4) teacher override — clears the
/// <c>MaxAttempts</c> cap for one submission by stamping
/// <see cref="AssignmentSubmission.AttemptLimitOverriddenAt"/> and
/// <see cref="AssignmentSubmission.AttemptLimitOverriddenBy"/>.
/// Mirrors the <c>EnableStudentSubmissionCommandHandler</c> shape:
/// fetch → domain method → Update + SaveChangesAsync +
/// cache.RemoveByTagAsync + structured log.
/// </summary>
public sealed class OverrideStudentSubmissionAttemptsCommandHandler(
    ISubmissionRepository submissionRepository,
    HybridCache cache,
    ILogger<OverrideStudentSubmissionAttemptsCommandHandler> logger) : ICommandHandler<OverrideStudentSubmissionAttemptsCommand>
{
    public async Task HandleAsync(OverrideStudentSubmissionAttemptsCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling OverrideStudentSubmissionAttempts for submission {SubmissionId}", command.SubmissionId);

        var submission = await submissionRepository.GetSubmissionAsync(command.SubmissionId, cancellationToken)
            ?? throw new SubmissionNotFoundException(command.SubmissionId);

        submission.OverrideAttemptLimit(command.TeacherId);
        submissionRepository.Update(submission);
        await submissionRepository.SaveChangesAsync(cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        logger.LogInformation("Teacher {TeacherId} overrode the attempt cap on submission {SubmissionId}",
            command.TeacherId, command.SubmissionId);
    }
}
