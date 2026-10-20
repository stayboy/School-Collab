using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.DenyAssignmentCommand;

/// <summary>Handles <see cref="DenyAssignmentCommand"/> (round assignment-approval-workflow /
/// spec §3.1, Q10). The domain method records the optional reason, clears the submission stamp and
/// moves the row to the terminal <c>Denied</c> state, which releases the edit hold while leaving
/// publish / schedule refused by the existing approval guards. Mirrors
/// <see cref="ApproveAssignmentCommand.ApproveAssignmentCommandHandler"/> — including the ar-24
/// cross-tenant rejection, because a denial is an approver decision and the tenant boundary must
/// hold for it exactly as it does for an approval.</summary>
public sealed class DenyAssignmentCommandHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ICurrentUser currentUser,
    IFeatureFlagService featureFlags,
    ILogger<DenyAssignmentCommandHandler> logger) : ICommandHandler<DenyAssignmentCommand>
{
    public async Task HandleAsync(DenyAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling DenyAssignment {Id}", command.AssignmentId);

        var assignment = await repository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        // ar-20 P1-6 principal-wins keyed on AUTH MODE (see ApproveAssignmentCommandHandler).
        var approverId = currentUser.TeacherId
            ?? (await IsRealAuthAsync(cancellationToken)
                ? throw new MissingTeacherPrincipalException(nameof(DenyAssignmentCommand))
                : command.ApproverId);

        // ar-24 cross-tenant rejection: the acting approver's tenant must match the assignment's.
        if (currentUser.CurrentTenant.TenantId != assignment.TenantId)
            throw new TeacherTenantMismatchException(
                command.AssignmentId, "assignment", currentUser.CurrentTenant.TenantId, assignment.TenantId);

        assignment.Deny(approverId, command.Reason);

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        assignment.ClearDomainEvents();

        logger.LogInformation("Assignment {Id} denied by {ApproverId}", assignment.Id, approverId);
    }

    private async Task<bool> IsRealAuthAsync(CancellationToken ct)
        => !await featureFlags.IsEnabledAsync(FeatureFlagKeys.DisableOIDCAuth, ct);
}
