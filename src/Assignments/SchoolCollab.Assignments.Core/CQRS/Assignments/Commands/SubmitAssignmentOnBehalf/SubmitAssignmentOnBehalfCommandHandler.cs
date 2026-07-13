using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentOnBehalf;

public sealed class SubmitAssignmentOnBehalfCommandHandler(
    ISubmissionRepository submissionRepository,
    ITenantProvider tenantProvider,
    ILogger<SubmitAssignmentOnBehalfCommandHandler> logger) : ICommandHandler<SubmitAssignmentOnBehalfCommand>
{
    public async Task HandleAsync(SubmitAssignmentOnBehalfCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling SubmitAssignmentOnBehalf for assignment {AssignmentId} / student {StudentId}",
            command.AssignmentId, command.StudentId);

        var gate = await submissionRepository.GetGateByAssignmentStudentAsync(command.AssignmentId, command.StudentId, cancellationToken)
            ?? throw new GuardianSubmissionGateNotFoundException(command.AssignmentId, command.StudentId);

        if (!gate.SubmissionEnabledForStudent)
            throw new InvalidOperationException(
                "Guardian submission is not enabled for this student. The Primary guardian must review the gate first.");

        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var now = DateTimeOffset.UtcNow;

        var submission = await submissionRepository.GetSubmissionByAssignmentStudentAsync(command.AssignmentId, command.StudentId, cancellationToken);
        if (submission is null)
        {
            submission = AssignmentSubmission.Create(tenantId, command.AssignmentId, command.StudentId, gate.Id);
            submissionRepository.Add(submission);
        }

        var newVersion = submission.CurrentVersionNumber + 1;
        var version = AssignmentSubmissionVersion.Create(
            tenantId, submission.Id, command.AssignmentId, command.StudentId,
            newVersion, SubmissionSource.GuardianOnBehalf, command.GuardianId, now, command.Content);
        submissionRepository.Add(version);

        submission.RecordSubmission(newVersion, SubmissionSource.GuardianOnBehalf, command.GuardianId, now);
        gate.SubmitOnBehalf(command.GuardianId, command.Content);

        submissionRepository.Update(gate);
        submissionRepository.Update(submission);
        await submissionRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Guardian {GuardianId} submitted assignment {AssignmentId} on behalf of student {StudentId} (v{Version})",
            command.GuardianId, command.AssignmentId, command.StudentId, newVersion);
    }
}
