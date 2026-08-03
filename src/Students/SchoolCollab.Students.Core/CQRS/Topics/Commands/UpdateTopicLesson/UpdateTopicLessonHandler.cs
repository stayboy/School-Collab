using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.UpdateTopicLesson;

public sealed class UpdateTopicLessonHandler(StudentsDbContext db) : ICommandHandler<UpdateTopicLesson, TopicLessonDto>
{
    public async Task<TopicLessonDto> HandleAsync(UpdateTopicLesson command, CancellationToken ct = default)
    {
        var lesson = await db.TopicLessons.FindAsync(new object[] { command.Id }, ct);
        if (lesson == null) throw new KeyNotFoundException($"Topic Lesson {command.Id} not found.");

        lesson.Update(command.Name, command.Description, command.StartDate, command.EndDate, command.DisplayOrder);
        await db.SaveChangesAsync(ct);

        return new TopicLessonDto(
            lesson.Id,
            lesson.TopicId,
            lesson.StrandId,
            lesson.Name,
            lesson.Description,
            lesson.StartDate,
            lesson.EndDate,
            lesson.IsOpenEnded,
            lesson.DisplayOrder,
            lesson.CreatedAt,
            lesson.UpdatedAt);
    }
}