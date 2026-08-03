using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Commands.AssignStudentTopic;

public sealed class AssignStudentTopicHandler(
    IStudentTopicAssignmentRepository repository,
    HybridCache cache,
    ILogger<AssignStudentTopicHandler> logger) : ICommandHandler<AssignStudentTopic, Guid>
{
    public async Task<Guid> HandleAsync(AssignStudentTopic command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling AssignStudentTopic for student {StudentId} topic {TopicId}", command.StudentId, command.TopicId);

        var assignment = StudentTopicAssignment.Create(
            command.StudentId,
            command.TopicId,
            command.PeriodId,
            command.IsOverride,
            command.SourceType);

        await repository.AddAsync(assignment, cancellationToken);
        assignment.ClearDomainEvents();
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("StudentTopicAssignment {Id} created", assignment.Id);
        return assignment.Id;
    }
}