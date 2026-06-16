using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.Commands.DeleteAssignment;

public sealed class DeleteAssignmentHandler(
    AssignmentsDbContext db,
    HybridCache cache,
    ILogger<DeleteAssignmentHandler> logger) : ICommandHandler<DeleteAssignmentCommand>
{
    public async Task HandleAsync(DeleteAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling DeleteAssignment {Id}", command.Id);

        var assignment = await db.Assignments.FindAsync([command.Id], cancellationToken)
            ?? throw new AssignmentNotFoundException(command.Id);

        if (assignment.Status != Domain.AssignmentStatus.Draft)
            throw new InvalidOperationException("Only draft assignments can be deleted.");

        db.Assignments.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        logger.LogInformation("Assignment {Id} deleted", command.Id);
    }
}