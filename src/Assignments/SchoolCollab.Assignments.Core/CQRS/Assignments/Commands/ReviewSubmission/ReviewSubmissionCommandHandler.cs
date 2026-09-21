using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewSubmission;

public sealed class ReviewSubmissionCommandHandler(
    ISubmissionRepository submissionRepository,
    IAssignmentRepository assignmentRepository,
    ITenantProvider tenantProvider,
    ICurrentUser currentUser,
    IFeatureFlagService featureFlags,
    ILogger<ReviewSubmissionCommandHandler> logger) : ICommandHandler<ReviewSubmissionCommand>
{
    public async Task HandleAsync(ReviewSubmissionCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ReviewSubmission {SubmissionId} by teacher {TeacherId}",
            command.SubmissionId, command.TeacherId);

        var submission = await submissionRepository.GetSubmissionAsync(command.SubmissionId, cancellationToken)
            ?? throw new SubmissionNotFoundException(command.SubmissionId);

        // Authorization (spec §8/§10): only the assignment's creating teacher can review.
        var assignment = await assignmentRepository.GetAsync(submission.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(submission.AssignmentId);

        // ar-24 claim-wins keyed on AUTH MODE (ar-20 P1-6 pattern): a teacher_id claim on the
        // principal overrides the wire body field; in real-auth (OIDC/bearer) a missing claim
        // is REJECTED; in TestAuth/dev the request field is honored.
        var teacherId = currentUser.TeacherId
            ?? (await IsRealAuthAsync(cancellationToken)
                ? throw new MissingTeacherPrincipalException(nameof(ReviewSubmissionCommand))
                : command.TeacherId);

        // ar-24 cross-tenant rejection: the acting teacher's tenant must match the assignment's.
        if (currentUser.CurrentTenant.TenantId != assignment.TenantId)
            throw new TeacherTenantMismatchException(
                assignment.Id, "assignment", currentUser.CurrentTenant.TenantId, assignment.TenantId);

        if (assignment.CreatedByTeacherId != teacherId)
            throw new UnauthorizedAccessException(
                $"Only the creating teacher ({assignment.CreatedByTeacherId}) can review submission {command.SubmissionId}.");

        var tenantId = tenantProvider.GetTenantContext().TenantId;

        var review = SubmissionReview.Create(
            tenantId,
            submission.Id,
            submission.AssignmentId,
            submission.StudentId,
            teacherId,
            command.Score,
            command.Grade,
            command.Comments);
        submissionRepository.Add(review);

        var hasOutcome = command.Score.HasValue || !string.IsNullOrWhiteSpace(command.Grade);
        submission.ApplyReview(hasOutcome ? ReviewState.Graded : ReviewState.Reviewed);
        submissionRepository.Update(submission);
        await submissionRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Submission {SubmissionId} reviewed by teacher {TeacherId} (state={State})",
            submission.Id, teacherId, submission.ReviewState);
    }

    private async Task<bool> IsRealAuthAsync(CancellationToken ct)
        => !await featureFlags.IsEnabledAsync(FeatureFlagKeys.DisableOIDCAuth, ct);
}
