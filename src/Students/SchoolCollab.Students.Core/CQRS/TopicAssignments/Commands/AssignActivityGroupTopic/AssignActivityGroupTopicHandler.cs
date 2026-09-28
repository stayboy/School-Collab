using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignActivityGroupTopic;

/// <summary>
/// Creates the activity-group ↔ topic bridge row.
/// <c>command.PeriodId</c> is <b>accepted and ignored</b>
/// (subject-period-exception-model.md, 2026-09-26): the bridge carries no period
/// scope, so nothing is validated and nothing is persisted. The FR-56 span rule
/// moved to the block side (<see cref="Domain.SubjectEnrollmentException"/>).
/// </summary>
public sealed class AssignActivityGroupTopicHandler(
    IActivityGroupTopicAssignmentRepository repository,
    HybridCache cache,
    ILogger<AssignActivityGroupTopicHandler> logger) : ICommandHandler<AssignActivityGroupTopic, Guid>
{
    public async Task<Guid> HandleAsync(AssignActivityGroupTopic command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling AssignActivityGroupTopic for group {ActivityGroupId} topic {TopicId}", command.ActivityGroupId, command.TopicId);

        // Duplicate guard. The retired period term is dropped from the predicate:
        // PeriodId is ignored, so the effective scope is always the year-spanning
        // (null) one — and ix_topic_assignments_tenant_group_topic_unique permits at
        // most one live bridge row per (tenant, group, topic) anyway, so a
        // period-scoped comparison could only ever produce a raw 23505/500.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var active = await repository.ListByActivityGroupAsync(command.ActivityGroupId, today, cancellationToken);
        if (active.Any(a => a.TopicId == command.TopicId))
            throw new DuplicateTopicAssignmentException(command.ActivityGroupId, command.TopicId, periodId: null);

        var assignment = ActivityGroupTopicAssignment.Create(
            command.ActivityGroupId,
            command.TopicId,
            command.StartDate,
            command.EndDate,
            command.TopicStrandId,
            periodId: null);

        await repository.AddAsync(assignment, cancellationToken);
        assignment.ClearDomainEvents();
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("ActivityGroupTopicAssignment {Id} created", assignment.Id);
        return assignment.Id;
    }
}
