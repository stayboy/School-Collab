using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.AutoReturnAssignmentCommand;

/// <summary>Handles <see cref="AutoReturnAssignmentCommand"/> (round assignment-approval-workflow /
/// spec §3.4). Dumb by design — the same domain transition as an approver's Reject ("sent back for
/// redo"), attributed to the sweep's system sentinel and carrying the sweep's reason; the caller has
/// already decided that the window expired or the due date passed. Mirrors the
/// <c>ArchiveAssignmentCommandHandler</c> shape: no principal, no policy resolve, no broadcaster
/// call.</summary>
public sealed class AutoReturnAssignmentCommandHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ILogger<AutoReturnAssignmentCommandHandler> logger) : ICommandHandler<AutoReturnAssignmentCommand>
{
    public async Task HandleAsync(AutoReturnAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling AutoReturnAssignment {Id}", command.AssignmentId);

        var assignment = await repository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        assignment.Reject(command.ApproverId, command.Reason);

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        assignment.ClearDomainEvents();

        logger.LogInformation(
            "Assignment {Id} returned for redo by the approval sweep ({Reason})", assignment.Id, command.Reason);
    }
}
