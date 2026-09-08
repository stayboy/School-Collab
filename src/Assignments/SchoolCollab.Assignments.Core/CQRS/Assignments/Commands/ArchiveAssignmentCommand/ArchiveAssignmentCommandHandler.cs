using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ArchiveAssignmentCommand;

/// <summary>Handles <see cref="ArchiveAssignmentCommand"/> (WS-A2 /
/// spec §7 Q6). Sweep-only — no recipients/gates to rebuild, no
/// broadcaster call (archived is read-only). Mirrors the
/// <c>CloseAssignmentCommandHandler</c> shape minus the
/// integration-event enqueue (decisions (c)/(g) name domain events
/// only).</summary>
public sealed class ArchiveAssignmentCommandHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ILogger<ArchiveAssignmentCommandHandler> logger) : ICommandHandler<ArchiveAssignmentCommand>
{
    public async Task HandleAsync(ArchiveAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ArchiveAssignment {Id}", command.AssignmentId);

        var assignment = await repository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        assignment.Archive();

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} archived", assignment.Id);
    }
}
