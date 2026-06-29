using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewAssignmentCommand;

public sealed class ReviewAssignmentCommandHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ILogger<ReviewAssignmentCommandHandler> logger) : ICommandHandler<ReviewAssignmentCommand>
{
    public async Task HandleAsync(ReviewAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ReviewAssignment {Id}", command.Id);

        var assignment = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.Id);

        assignment.AddReview(command.TeacherId, command.Score, command.Comments);
        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        logger.LogInformation("Review added to Assignment {Id} by teacher {TeacherId}", assignment.Id, command.TeacherId);
    }
}