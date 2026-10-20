using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.DeleteAssignmentCommand;

public sealed class DeleteAssignmentCommandHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ILogger<DeleteAssignmentCommandHandler> logger) : ICommandHandler<DeleteAssignmentCommand>
{
    public async Task HandleAsync(DeleteAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling DeleteAssignment {Id}", command.Id);

        var assignment = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.Id);

        // Round assignment-approval-workflow (spec §3.2, Q9): deleting the row under review — a held
        // authoring write path, so it refuses while the row awaits an approval decision.
        assignment.EnsureNotAwaitingApproval();

        if (assignment.Status != Domain.AssignmentStatus.Draft)
            throw new InvalidOperationException("Only draft assignments can be deleted.");

        await repository.DeleteAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        logger.LogInformation("Assignment {Id} deleted", command.Id);
    }
}
