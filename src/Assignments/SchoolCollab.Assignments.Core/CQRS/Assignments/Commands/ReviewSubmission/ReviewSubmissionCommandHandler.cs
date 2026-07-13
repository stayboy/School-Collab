using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewSubmission;

public sealed class ReviewSubmissionCommandHandler(
    ISubmissionRepository submissionRepository,
    IAssignmentRepository assignmentRepository,
    ITenantProvider tenantProvider,
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
        if (assignment.CreatedByTeacherId != command.TeacherId)
            throw new UnauthorizedAccessException(
                $"Only the creating teacher ({assignment.CreatedByTeacherId}) can review submission {command.SubmissionId}.");

        var tenantId = tenantProvider.GetTenantContext().TenantId;

        var review = SubmissionReview.Create(
            tenantId,
            submission.Id,
            submission.AssignmentId,
            submission.StudentId,
            command.TeacherId,
            command.Score,
            command.Grade,
            command.Comments);
        submissionRepository.Add(review);

        var hasOutcome = command.Score.HasValue || !string.IsNullOrWhiteSpace(command.Grade);
        submission.ApplyReview(hasOutcome ? ReviewState.Graded : ReviewState.Reviewed);
        submissionRepository.Update(submission);
        await submissionRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Submission {SubmissionId} reviewed by teacher {TeacherId} (state={State})",
            submission.Id, command.TeacherId, submission.ReviewState);
    }
}
