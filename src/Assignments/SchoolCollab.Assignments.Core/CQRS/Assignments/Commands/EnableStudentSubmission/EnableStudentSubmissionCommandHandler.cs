using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.EnableStudentSubmission;

public sealed class EnableStudentSubmissionCommandHandler(
    ISubmissionRepository submissionRepository,
    HybridCache cache,
    ILogger<EnableStudentSubmissionCommandHandler> logger) : ICommandHandler<EnableStudentSubmissionCommand>
{
    public async Task HandleAsync(EnableStudentSubmissionCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling EnableStudentSubmission for gate {GateId}", command.GateId);

        var gate = await submissionRepository.GetGateAsync(command.GateId, cancellationToken)
            ?? throw new GuardianSubmissionGateNotFoundException(command.GateId);

        gate.EnableForStudent();
        submissionRepository.Update(gate);
        await submissionRepository.SaveChangesAsync(cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        logger.LogInformation("Guardian submission gate {GateId} enabled for student submission", gate.Id);
    }
}