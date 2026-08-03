using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignGradeTopic;

public sealed class AssignGradeTopicHandler(
    IGradeTopicAssignmentRepository repository,
    HybridCache cache,
    ILogger<AssignGradeTopicHandler> logger) : ICommandHandler<AssignGradeTopic, Guid>
{
    public async Task<Guid> HandleAsync(AssignGradeTopic command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling AssignGradeTopic for grade {GradeLevelId} topic {TopicId}", command.GradeLevelId, command.TopicId);

        var assignment = GradeTopicAssignment.Create(
            command.GradeLevelId,
            command.TopicId,
            command.StartDate,
            command.EndDate,
            command.TopicStrandId,
            command.TopicLessonId);

        await repository.AddAsync(assignment, cancellationToken);
        assignment.ClearDomainEvents();
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("GradeTopicAssignment {Id} created", assignment.Id);
        return assignment.Id;
    }
}
