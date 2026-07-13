using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateStudentSubmission;

public sealed class CreateStudentSubmissionCommandHandler(
    IAssignmentRepository assignmentRepository,
    ISubmissionRepository submissionRepository,
    ITenantProvider tenantProvider,
    ILogger<CreateStudentSubmissionCommandHandler> logger) : ICommandHandler<CreateStudentSubmissionCommand>
{
    public async Task HandleAsync(CreateStudentSubmissionCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling CreateStudentSubmission for assignment {AssignmentId} / student {StudentId}",
            command.AssignmentId, command.StudentId);

        var assignment = await assignmentRepository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        var gate = await submissionRepository.GetGateByAssignmentStudentAsync(command.AssignmentId, command.StudentId, cancellationToken);

        // Student self-submit is allowed only when MandatoryReview == false OR the
        // Primary guardian has enabled the gate (spec §4.10).
        if (assignment.MandatoryReview && gate?.SubmissionEnabledForStudent != true)
            throw new UnauthorizedAccessException(
                "Student self-submit is not enabled. The Primary guardian must review the gate first, or the assignment must not require mandatory review.");

        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var now = DateTimeOffset.UtcNow;

        var submission = await submissionRepository.GetSubmissionByAssignmentStudentAsync(command.AssignmentId, command.StudentId, cancellationToken);
        if (submission is null)
        {
            submission = AssignmentSubmission.Create(tenantId, command.AssignmentId, command.StudentId, gate?.Id);
            submissionRepository.Add(submission);
        }

        var newVersion = submission.CurrentVersionNumber + 1;
        var version = AssignmentSubmissionVersion.Create(
            tenantId, submission.Id, command.AssignmentId, command.StudentId,
            newVersion, SubmissionSource.Student, null, now, command.Content);
        submissionRepository.Add(version);

        submission.RecordSubmission(newVersion, SubmissionSource.Student, null, now);
        submissionRepository.Update(submission);
        await submissionRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Student {StudentId} submitted assignment {AssignmentId} (v{Version})",
            command.StudentId, command.AssignmentId, newVersion);
    }
}