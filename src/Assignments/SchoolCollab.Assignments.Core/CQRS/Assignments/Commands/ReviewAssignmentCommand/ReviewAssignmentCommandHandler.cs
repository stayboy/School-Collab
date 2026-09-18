using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewAssignmentCommand;

public sealed class ReviewAssignmentCommandHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ICurrentUser currentUser,
    IFeatureFlagService featureFlags,
    ILogger<ReviewAssignmentCommandHandler> logger) : ICommandHandler<ReviewAssignmentCommand>
{
    public async Task HandleAsync(ReviewAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ReviewAssignment {Id}", command.Id);

        var assignment = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.Id);

        // ar-20 P1-6 principal-wins keyed on AUTH MODE (see ApproveAssignmentCommandHandler).
        var teacherId = currentUser.TeacherId
            ?? (await IsRealAuthAsync(cancellationToken)
                ? throw new MissingTeacherPrincipalException(nameof(ReviewAssignmentCommand))
                : command.TeacherId);
        assignment.AddReview(teacherId, command.Score, command.Comments);
        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        logger.LogInformation("Review added to Assignment {Id} by teacher {TeacherId}", assignment.Id, teacherId);
    }

    private async Task<bool> IsRealAuthAsync(CancellationToken ct)
        => !await featureFlags.IsEnabledAsync(FeatureFlagKeys.DisableOIDCAuth, ct);
}