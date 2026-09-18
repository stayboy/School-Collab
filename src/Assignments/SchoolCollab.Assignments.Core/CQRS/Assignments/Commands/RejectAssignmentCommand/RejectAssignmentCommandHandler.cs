using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;
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
    ICurrentUser currentUser,
    IFeatureFlagService featureFlags,
    ILogger<RejectAssignmentCommandHandler> logger) : ICommandHandler<RejectAssignmentCommand>
{
    public async Task HandleAsync(RejectAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling RejectAssignment {Id}", command.AssignmentId);

        var assignment = await repository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        // ar-20 P1-6 principal-wins keyed on AUTH MODE (see ApproveAssignmentCommandHandler).
        var approverId = currentUser.TeacherId
            ?? (await IsRealAuthAsync(cancellationToken)
                ? throw new MissingTeacherPrincipalException(nameof(RejectAssignmentCommand))
                : command.ApproverId);
        assignment.Reject(approverId);

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} rejected by {ApproverId}", assignment.Id, approverId);
    }

    private async Task<bool> IsRealAuthAsync(CancellationToken ct)
        => !await featureFlags.IsEnabledAsync(FeatureFlagKeys.DisableOIDCAuth, ct);
}
