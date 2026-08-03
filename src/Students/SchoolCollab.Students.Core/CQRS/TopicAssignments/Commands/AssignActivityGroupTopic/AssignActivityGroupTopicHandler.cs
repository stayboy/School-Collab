using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignActivityGroupTopic;

public sealed class AssignActivityGroupTopicHandler(
    IActivityGroupTopicAssignmentRepository repository,
    HybridCache cache,
    ILogger<AssignActivityGroupTopicHandler> logger) : ICommandHandler<AssignActivityGroupTopic, Guid>
{
    public async Task<Guid> HandleAsync(AssignActivityGroupTopic command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling AssignActivityGroupTopic for group {ActivityGroupId} topic {TopicId}", command.ActivityGroupId, command.TopicId);

        var assignment = ActivityGroupTopicAssignment.Create(
            command.ActivityGroupId,
            command.TopicId,
            command.StartDate,
            command.EndDate,
            command.TopicStrandId,
            command.TopicLessonId);

        await repository.AddAsync(assignment, cancellationToken);
        assignment.ClearDomainEvents();
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("ActivityGroupTopicAssignment {Id} created", assignment.Id);
        return assignment.Id;
    }
}
