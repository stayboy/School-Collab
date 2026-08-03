using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.RemoveTopicLesson;

public sealed class RemoveTopicLessonHandler(StudentsDbContext db) : ICommandHandler<RemoveTopicLesson>
{
    public async Task HandleAsync(RemoveTopicLesson command, CancellationToken ct = default)
    {
        var lesson = await db.TopicLessons.FindAsync(new object[] { command.Id }, ct);
        if (lesson == null) throw new KeyNotFoundException($"Topic Lesson {command.Id} not found.");

        db.TopicLessons.Remove(lesson);
        await db.SaveChangesAsync(ct);
    }
}