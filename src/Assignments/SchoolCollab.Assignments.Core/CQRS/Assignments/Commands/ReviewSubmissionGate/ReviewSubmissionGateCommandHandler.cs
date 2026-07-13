using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewSubmissionGate;

public sealed class ReviewSubmissionGateCommandHandler(
    ISubmissionRepository submissionRepository,
    ILogger<ReviewSubmissionGateCommandHandler> logger) : ICommandHandler<ReviewSubmissionGateCommand>
{
    public async Task HandleAsync(ReviewSubmissionGateCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ReviewSubmissionGate {GateId} (approve={Approve})", command.GateId, command.Approve);

        var gate = await submissionRepository.GetGateAsync(command.GateId, cancellationToken)
            ?? throw new GuardianSubmissionGateNotFoundException(command.GateId);

        gate.Review(command.ReviewerGuardianId, command.Approve, command.Comment);
        submissionRepository.Update(gate);
        await submissionRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Guardian submission gate {GateId} reviewed by {GuardianId} (approve={Approve})",
            gate.Id, command.ReviewerGuardianId, command.Approve);
    }
}
