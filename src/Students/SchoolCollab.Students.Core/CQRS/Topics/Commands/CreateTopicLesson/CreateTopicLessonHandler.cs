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
        var lesson = TopicLesson.Create(
            command.TopicId,
            command.Name,
            command.Description,
            command.StartDate,
            command.EndDate,
            command.DisplayOrder);

        db.TopicLessons.Add(lesson);
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