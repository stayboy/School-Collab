using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ScheduleAssignmentCommand;

/// <summary>Handles <see cref="ScheduleAssignmentCommand"/> (WS-A2 /
/// spec §3.5 step 2). Resolves the effective assignment policy's
/// <c>RequiresApprovalBeforePublish</c> OR'd with the
/// <c>FEATURE:RequireAssignmentApproval</c> flag (D3, one release) and threads
/// <c>approvalRequired</c> into the domain — the typed
/// <see cref="AssignmentApprovalRequiredException"/> surfaces from
/// <c>Assignment.Schedule</c> when either is on but the row is
/// not yet approved.</summary>
public sealed class ScheduleAssignmentCommandHandler(
    IAssignmentRepository repository,
    IFeatureFlagService featureFlags,
    IAssignmentPolicyResolver assignmentPolicyResolver,
    HybridCache cache,
    ILogger<ScheduleAssignmentCommandHandler> logger) : ICommandHandler<ScheduleAssignmentCommand>
{
    public async Task HandleAsync(ScheduleAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ScheduleAssignment {Id}", command.AssignmentId);

        var assignment = await repository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        // D3: the policy field OR the flag. The resolver is fail-open (a failed fetch
        // resolves false), so the gate can only be turned ON by the flag or by an
        // explicitly configured policy field.
        var assignmentPolicy = await assignmentPolicyResolver.ResolveAsync(assignment.GradeLevelId, cancellationToken);
        var approvalRequired = assignmentPolicy.RequiresApprovalBeforePublish
            || await featureFlags.IsEnabledAsync(FeatureFlagKeys.RequireAssignmentApproval, cancellationToken);

        assignment.Schedule(command.AvailableFromUtc, approvalRequired);

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} scheduled for {AvailableFromUtc}", assignment.Id, command.AvailableFromUtc);
    }
}
