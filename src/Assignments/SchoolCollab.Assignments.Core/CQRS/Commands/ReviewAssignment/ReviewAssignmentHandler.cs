using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.Commands.ReviewAssignment;

public sealed class ReviewAssignmentHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ILogger<ReviewAssignmentHandler> logger) : ICommandHandler<ReviewAssignmentCommand>
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