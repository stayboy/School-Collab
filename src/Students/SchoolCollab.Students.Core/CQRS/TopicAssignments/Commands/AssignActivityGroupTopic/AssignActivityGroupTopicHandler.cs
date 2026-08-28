using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignActivityGroupTopic;

public sealed class AssignActivityGroupTopicHandler(
    IActivityGroupTopicAssignmentRepository repository,
    IActivityGroupRepository groupRepository,
    IPeriodRepository periodRepository,
    HybridCache cache,
    ILogger<AssignActivityGroupTopicHandler> logger) : ICommandHandler<AssignActivityGroupTopic, Guid>
{
    public async Task<Guid> HandleAsync(AssignActivityGroupTopic command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling AssignActivityGroupTopic for group {ActivityGroupId} topic {TopicId}", command.ActivityGroupId, command.TopicId);

        // ── Rev. 6 FR-56: the group's EnrollmentSpan dictates whether/which period
        //    a group-owned topic's PeriodId may reference.
        await TopicAssignmentPeriodValidator.ValidateGroupPeriodAsync(
            command.ActivityGroupId, command.PeriodId, groupRepository, periodRepository, cancellationToken);

        // ── Rev. 6: reject a duplicate active (group, topic, period) assignment.
        //    Period validation runs first so an invalid period still 422s before
        //    the duplicate guard (which would otherwise 409).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var active = await repository.ListByActivityGroupAsync(command.ActivityGroupId, today, cancellationToken);
        if (active.Any(a => a.TopicId == command.TopicId && a.PeriodId == command.PeriodId))
            throw new DuplicateTopicAssignmentException(command.ActivityGroupId, command.TopicId, command.PeriodId);

        var assignment = ActivityGroupTopicAssignment.Create(
            command.ActivityGroupId,
            command.TopicId,
            command.StartDate,
            command.EndDate,
            command.TopicStrandId,
            command.PeriodId);

        await repository.AddAsync(assignment, cancellationToken);
        assignment.ClearDomainEvents();
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("ActivityGroupTopicAssignment {Id} created", assignment.Id);
        return assignment.Id;
    }
}