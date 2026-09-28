using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.UpdateTopicAssignmentPeriod;

/// <summary>
/// Updates the <see cref="TopicAssignment.PeriodId"/> of an existing assignment,
/// reusing the exact creation-time period validation (FR-56/57) via the shared
/// <see cref="TopicAssignmentPeriodValidator"/>. Works for both the grade and
/// activity-group subtype through the TPH root.
///
/// <para><b>DEPRECATED (2026-09-26)</b> — subject-period-exception-model.md. The
/// bridge row no longer carries period meaning: availability is "bridge exists AND
/// no <see cref="Domain.SubjectEnrollmentException"/> on the active period". The route
/// (<c>PUT /topic-assignments/{id}/period</c>) and this handler stay functional for
/// wire compatibility but no UI calls them any more. This is the only remaining
/// caller of the FR-56/57 bridge validators.</para>
/// </summary>
public sealed class UpdateTopicAssignmentPeriodHandler(
    StudentsDbContext db,
    IPeriodRepository periodRepository,
    IActivityGroupRepository groupRepository,
    HybridCache cache,
    ILogger<UpdateTopicAssignmentPeriodHandler> logger) : ICommandHandler<UpdateTopicAssignmentPeriod, TopicAssignmentDto>
{
    public async Task<TopicAssignmentDto> HandleAsync(UpdateTopicAssignmentPeriod command, CancellationToken ct = default)
    {
        var assignment = await db.TopicAssignments.FindAsync(new object[] { command.AssignmentId }, ct);
        if (assignment == null) throw new KeyNotFoundException($"TopicAssignment {command.AssignmentId} not found.");

        // Dispatch validation by subtype — the owner (grade/group) is unchanged by
        // this command, so validate against the assignment's own owner.
        switch (assignment)
        {
            case GradeTopicAssignment grade:
                await TopicAssignmentPeriodValidator.ValidateGradePeriodAsync(command.PeriodId, periodRepository, ct);
                break;
            case ActivityGroupTopicAssignment group:
                await TopicAssignmentPeriodValidator.ValidateGroupPeriodAsync(
                    group.ActivityGroupId, command.PeriodId, groupRepository, periodRepository, ct);
                break;
            default:
                throw new InvalidOperationException($"Unknown topic assignment subtype '{assignment.GetType().Name}'.");
        }

        assignment.UpdatePeriod(command.PeriodId); // DEPRECATED write path — kept for wire compatibility only.
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync("students", ct);

        logger.LogInformation("TopicAssignment {Id} period updated to {PeriodId}", assignment.Id, command.PeriodId);

        return assignment switch
        {
            GradeTopicAssignment grade => new TopicAssignmentDto(
                grade.Id, "grade", grade.GradeLevelId, null,
                grade.TopicId, grade.StartDate, grade.EndDate,
                grade.TopicStrandId, grade.PeriodId, grade.CreatedAt, grade.UpdatedAt),
            ActivityGroupTopicAssignment group => new TopicAssignmentDto(
                group.Id, "activity_group", null, group.ActivityGroupId,
                group.TopicId, group.StartDate, group.EndDate,
                group.TopicStrandId, group.PeriodId, group.CreatedAt, group.UpdatedAt),
            _ => throw new InvalidOperationException($"Unknown topic assignment subtype '{assignment.GetType().Name}'.")
        };
    }
}
