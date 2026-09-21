using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;
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
    ICurrentUser currentUser,
    IFeatureFlagService featureFlags,
    ILogger<OverrideStudentSubmissionAttemptsCommandHandler> logger) : ICommandHandler<OverrideStudentSubmissionAttemptsCommand>
{
    public async Task HandleAsync(OverrideStudentSubmissionAttemptsCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling OverrideStudentSubmissionAttempts for submission {SubmissionId}", command.SubmissionId);

        var submission = await submissionRepository.GetSubmissionAsync(command.SubmissionId, cancellationToken)
            ?? throw new SubmissionNotFoundException(command.SubmissionId);

        // ar-20 P1-6 principal-wins keyed on AUTH MODE (see ApproveAssignmentCommandHandler).
        var teacherId = currentUser.TeacherId
            ?? (await IsRealAuthAsync(cancellationToken)
                ? throw new MissingTeacherPrincipalException(nameof(OverrideStudentSubmissionAttemptsCommand))
                : command.TeacherId);

        // ar-24 cross-tenant rejection: the acting teacher's tenant must match the submission's.
        if (currentUser.CurrentTenant.TenantId != submission.TenantId)
            throw new TeacherTenantMismatchException(
                command.SubmissionId, "submission", currentUser.CurrentTenant.TenantId, submission.TenantId);

        submission.OverrideAttemptLimit(teacherId);
        submissionRepository.Update(submission);
        await submissionRepository.SaveChangesAsync(cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        logger.LogInformation("Teacher {TeacherId} overrode the attempt cap on submission {SubmissionId}",
            teacherId, command.SubmissionId);
    }

    private async Task<bool> IsRealAuthAsync(CancellationToken ct)
        => !await featureFlags.IsEnabledAsync(FeatureFlagKeys.DisableOIDCAuth, ct);
}
