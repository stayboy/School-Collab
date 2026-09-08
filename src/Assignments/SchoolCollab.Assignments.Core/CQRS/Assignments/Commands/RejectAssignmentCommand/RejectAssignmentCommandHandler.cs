using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RejectAssignmentCommand;

/// <summary>Handles <see cref="RejectAssignmentCommand"/> (WS-A2 /
/// spec §7 Q2). The domain method clears any prior approve stamps
/// and raises <c>AssignmentRejectedEvent</c>. Mirrors the
/// <c>CloseAssignmentCommandHandler</c> shape.</summary>
public sealed class RejectAssignmentCommandHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ILogger<RejectAssignmentCommandHandler> logger) : ICommandHandler<RejectAssignmentCommand>
{
    public async Task HandleAsync(RejectAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling RejectAssignment {Id}", command.AssignmentId);

        var assignment = await repository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        assignment.Reject(command.ApproverId);

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} rejected by {ApproverId}", assignment.Id, command.ApproverId);
    }
}
