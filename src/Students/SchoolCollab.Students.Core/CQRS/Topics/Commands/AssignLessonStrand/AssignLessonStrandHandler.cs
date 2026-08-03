using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.AssignLessonStrand;

public sealed class AssignLessonStrandHandler(StudentsDbContext db) : ICommandHandler<AssignLessonStrand, TopicLessonDto>
{
    public async Task<TopicLessonDto> HandleAsync(AssignLessonStrand command, CancellationToken ct = default)
    {
        var lesson = await db.TopicLessons.FindAsync(new object[] { command.LessonId }, ct);
        if (lesson == null) throw new KeyNotFoundException($"Topic Lesson {command.LessonId} not found.");

        lesson.SetStrand(command.StrandId);
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