using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignGradeTopic;

/// <summary>
/// Creates the grade ↔ topic bridge row.
/// <c>command.PeriodId</c> is <b>accepted and ignored</b>
/// (subject-period-exception-model.md, 2026-09-26): the bridge carries no period
/// scope, so nothing is validated and nothing is persisted. The FR-57 validation
/// moved to the block side (<see cref="Domain.SubjectEnrollmentException"/>).
/// </summary>
public sealed class AssignGradeTopicHandler(
    IGradeTopicAssignmentRepository repository,
    HybridCache cache,
    ILogger<AssignGradeTopicHandler> logger) : ICommandHandler<AssignGradeTopic, Guid>
{
    public async Task<Guid> HandleAsync(AssignGradeTopic command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling AssignGradeTopic for grade {GradeLevelId} topic {TopicId}", command.GradeLevelId, command.TopicId);

        // DEPRECATED: command.PeriodId is accepted for back-compat and ignored.
        // The bridge row is deliberately period-less; a period exception is a
        // SubjectEnrollmentException row, not a bridge column.
        var assignment = GradeTopicAssignment.Create(
            command.GradeLevelId,
            command.TopicId,
            command.StartDate,
            command.EndDate,
            command.TopicStrandId,
            periodId: null);

        await repository.AddAsync(assignment, cancellationToken);
        assignment.ClearDomainEvents();
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("GradeTopicAssignment {Id} created", assignment.Id);
        return assignment.Id;
    }
}
