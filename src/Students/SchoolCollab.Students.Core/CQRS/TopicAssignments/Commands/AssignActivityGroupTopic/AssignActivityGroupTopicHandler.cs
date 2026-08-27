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
        await ValidatePeriodAsync(command, cancellationToken);

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

    private async Task ValidatePeriodAsync(AssignActivityGroupTopic command, CancellationToken cancellationToken)
    {
        if (command.PeriodId is null)
            return; // null = date-based window (OpenEnded/DateRange, or period-aligned but no period set).

        var group = await groupRepository.GetAsync(command.ActivityGroupId, cancellationToken)
            ?? throw new ActivityGroupNotFoundException(command.ActivityGroupId);

        // OpenEnded/DateRange carry no period → PeriodId must be null (EC-23).
        var requiredType = group.Span switch
        {
            EnrollmentSpan.Termly => PeriodType.Term,
            EnrollmentSpan.Semester => PeriodType.Semester,
            EnrollmentSpan.WholeAcademicYear => PeriodType.AcademicYear,
            _ => (PeriodType?)null
        };

        if (requiredType is null)
            throw new TopicAssignmentPeriodException(
                $"An {group.Span} activity group topic assignment must not carry a PeriodId.", command.PeriodId);

        var period = await periodRepository.GetAsync(command.PeriodId.Value, cancellationToken)
            ?? throw new TopicAssignmentPeriodException($"Period '{command.PeriodId}' does not exist.", command.PeriodId);

        if (period.PeriodType != requiredType.Value)
            throw new TopicAssignmentPeriodException(
                $"A {group.Span} activity group topic requires a {requiredType} period, but '{command.PeriodId}' is a {period.PeriodType}.",
                command.PeriodId);
    }
}