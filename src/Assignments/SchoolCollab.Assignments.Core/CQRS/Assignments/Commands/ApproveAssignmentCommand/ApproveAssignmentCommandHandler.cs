using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ApproveAssignmentCommand;

/// <summary>Handles <see cref="ApproveAssignmentCommand"/> (WS-A2 /
/// spec §7 Q2). Approves a pending assignment — the domain method
/// stamps <c>ApprovedBy</c> + <c>ApprovedAt</c> and raises
/// <c>AssignmentApprovedEvent</c>. Mirrors the
/// <c>CloseAssignmentCommandHandler</c> shape.</summary>
public sealed class ApproveAssignmentCommandHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ILogger<ApproveAssignmentCommandHandler> logger) : ICommandHandler<ApproveAssignmentCommand>
{
    public async Task HandleAsync(ApproveAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ApproveAssignment {Id}", command.AssignmentId);

        var assignment = await repository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        assignment.Approve(command.ApproverId);

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} approved by {ApproverId}", assignment.Id, command.ApproverId);
    }
}
