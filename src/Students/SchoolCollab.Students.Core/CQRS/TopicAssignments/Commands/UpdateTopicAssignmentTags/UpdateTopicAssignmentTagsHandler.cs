using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.UpdateTopicAssignmentTags;

public sealed class UpdateTopicAssignmentTagsHandler(StudentsDbContext db) : ICommandHandler<UpdateTopicAssignmentTags, TopicAssignmentDto>
{
    public async Task<TopicAssignmentDto> HandleAsync(UpdateTopicAssignmentTags command, CancellationToken ct = default)
    {
        var assignment = await db.TopicAssignments.FindAsync(new object[] { command.AssignmentId }, ct);
        if (assignment == null) throw new KeyNotFoundException($"TopicAssignment {command.AssignmentId} not found.");

        assignment.UpdateTags(command.TopicStrandId);
        await db.SaveChangesAsync(ct);

        return assignment switch
        {
            GradeTopicAssignment grade => new TopicAssignmentDto(
                grade.Id, "grade", grade.GradeLevelId, null,
                grade.TopicId, grade.StartDate, grade.EndDate,
                grade.TopicStrandId, grade.CreatedAt, grade.UpdatedAt),
            ActivityGroupTopicAssignment group => new TopicAssignmentDto(
                group.Id, "activity_group", null, group.ActivityGroupId,
                group.TopicId, group.StartDate, group.EndDate,
                group.TopicStrandId, group.CreatedAt, group.UpdatedAt),
            _ => throw new InvalidOperationException($"Unknown topic assignment subtype '{assignment.GetType().Name}'.")
        };
    }
}
