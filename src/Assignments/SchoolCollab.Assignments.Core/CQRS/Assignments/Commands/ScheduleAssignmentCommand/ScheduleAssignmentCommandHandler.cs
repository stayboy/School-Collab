using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ScheduleAssignmentCommand;

/// <summary>Handles <see cref="ScheduleAssignmentCommand"/> (WS-A2 /
/// spec §3.5 step 2). Resolves the approval flag and threads
/// <c>approvalRequired</c> into the domain — the typed
/// <see cref="AssignmentApprovalRequiredException"/> surfaces from
/// <c>Assignment.Schedule</c> when the flag is on but the row is
/// not yet approved.</summary>
public sealed class ScheduleAssignmentCommandHandler(
    IAssignmentRepository repository,
    IFeatureFlagService featureFlags,
    HybridCache cache,
    ILogger<ScheduleAssignmentCommandHandler> logger) : ICommandHandler<ScheduleAssignmentCommand>
{
    public async Task HandleAsync(ScheduleAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ScheduleAssignment {Id}", command.AssignmentId);

        var assignment = await repository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        var approvalRequired = await featureFlags.IsEnabledAsync(FeatureFlagKeys.RequireAssignmentApproval, cancellationToken);

        assignment.Schedule(command.AvailableFromUtc, approvalRequired);

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} scheduled for {AvailableFromUtc}", assignment.Id, command.AvailableFromUtc);
    }
}
