using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopicLesson;

public sealed class CreateTopicLessonHandler(StudentsDbContext db) : ICommandHandler<CreateTopicLesson, TopicLessonDto>
{
    public async Task<TopicLessonDto> HandleAsync(CreateTopicLesson command, CancellationToken ct = default)
    {
        // A lesson is a strand that has a parent strand (strand-lesson-unification-plan.md).
        var lesson = TopicStrand.Create(
            command.TopicId,
            command.Name,
            command.Description,
            command.DisplayOrder,
            parentStrandId: command.StrandId,
            startDate: command.StartDate,
            endDate: command.EndDate);

        db.TopicStrands.Add(lesson);
        await db.SaveChangesAsync(ct);

        return TopicLessonDto.FromStrand(lesson);
    }
}
