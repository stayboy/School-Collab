using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentForApprovalCommand;

/// <summary>Handles <see cref="SubmitAssignmentForApprovalCommand"/>
/// (WS-A2 / spec §7 Q2). Mirrors the <c>CloseAssignmentCommandHandler</c>
/// shape — repository → domain method → save → cache eviction.</summary>
public sealed class SubmitAssignmentForApprovalCommandHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ILogger<SubmitAssignmentForApprovalCommandHandler> logger) : ICommandHandler<SubmitAssignmentForApprovalCommand>
{
    public async Task HandleAsync(SubmitAssignmentForApprovalCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling SubmitAssignmentForApproval {Id}", command.AssignmentId);

        var assignment = await repository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        // Round assignment-approval-workflow (spec §3.2, Q9): a resubmit — a held
        // authoring write path, so it refuses while the row awaits an approval decision.
        assignment.EnsureNotAwaitingApproval();

        assignment.SubmitForApproval();

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} submitted for approval", assignment.Id);
    }
}
