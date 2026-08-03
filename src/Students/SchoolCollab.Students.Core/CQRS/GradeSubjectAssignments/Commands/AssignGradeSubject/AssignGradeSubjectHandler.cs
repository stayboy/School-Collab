using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Commands.AssignGradeSubject;

public sealed class AssignGradeSubjectHandler(
    IGradeSubjectAssignmentRepository repository,
    HybridCache cache,
    ILogger<AssignGradeSubjectHandler> logger) : ICommandHandler<AssignGradeSubject, Guid>
{
    public async Task<Guid> HandleAsync(AssignGradeSubject command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling AssignGradeSubject for grade {GradeLevelId} group {ActivityGroupId} topic {TopicId}", command.GradeLevelId, command.ActivityGroupId, command.TopicId);

        var assignment = GradeSubjectAssignment.Create(
            command.GradeLevelId,
            command.ActivityGroupId,
            command.TopicId,
            command.StartDate,
            command.EndDate,
            command.TopicStrandId,
            command.TopicLessonId);

        await repository.AddAsync(assignment, cancellationToken);
        assignment.ClearDomainEvents();
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("GradeSubjectAssignment {Id} created", assignment.Id);
        return assignment.Id;
    }
}