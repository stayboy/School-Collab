using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.RemoveTopicAssignment;

public sealed class RemoveTopicAssignmentHandler(
    ITopicAssignmentRepository repository,
    HybridCache cache,
    ILogger<RemoveTopicAssignmentHandler> logger) : ICommandHandler<RemoveTopicAssignment>
{
    public async Task HandleAsync(RemoveTopicAssignment command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling RemoveTopicAssignment {Id}", command.Id);

        var assignment = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new InvalidOperationException($"TopicAssignment with ID '{command.Id}' not found.");

        // Grade/group↔topic assignments span multiple years, so we block/archive by
        // ending the effective period rather than hard-deleting the row. This
        // keeps the audit trail and any historical references intact.
        await repository.EndAsync(assignment, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("TopicAssignment {Id} ended (blocked/archived)", assignment.Id);
    }
}
