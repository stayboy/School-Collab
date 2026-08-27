using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignGradeTopic;

public sealed class AssignGradeTopicHandler(
    IGradeTopicAssignmentRepository repository,
    IPeriodRepository periodRepository,
    HybridCache cache,
    ILogger<AssignGradeTopicHandler> logger) : ICommandHandler<AssignGradeTopic, Guid>
{
    public async Task<Guid> HandleAsync(AssignGradeTopic command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling AssignGradeTopic for grade {GradeLevelId} topic {TopicId}", command.GradeLevelId, command.TopicId);

        // ── Rev. 6 FR-57: a grade-owned topic's PeriodId, when set, must be an
        //    AcademicYear or a Term/Semester within the active academic year.
        await ValidatePeriodAsync(command.PeriodId, cancellationToken);

        var assignment = GradeTopicAssignment.Create(
            command.GradeLevelId,
            command.TopicId,
            command.StartDate,
            command.EndDate,
            command.TopicStrandId,
            command.PeriodId);

        await repository.AddAsync(assignment, cancellationToken);
        assignment.ClearDomainEvents();
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("GradeTopicAssignment {Id} created", assignment.Id);
        return assignment.Id;
    }

    private async Task ValidatePeriodAsync(Guid? periodId, CancellationToken cancellationToken)
    {
        if (periodId is null)
            return; // null = year-spanning date-based delivery (back-compat).

        var period = await periodRepository.GetAsync(periodId.Value, cancellationToken)
            ?? throw new TopicAssignmentPeriodException($"Period '{periodId}' does not exist.", periodId);

        if (period.PeriodType == PeriodType.AcademicYear)
            return; // any academic year is a valid grade-topic period.

        // Term/Semester must belong to the tenant's active academic year (FR-57, EC-24).
        var activeYear = await periodRepository.GetActiveAcademicYearAsync(
            cancellationToken: cancellationToken);
        if (activeYear is null || period.ParentPeriodId != activeYear.Id)
            throw new TopicAssignmentPeriodException(
                $"Grade topic period '{periodId}' is a {period.PeriodType} outside the tenant's active academic year.", periodId);
    }
}