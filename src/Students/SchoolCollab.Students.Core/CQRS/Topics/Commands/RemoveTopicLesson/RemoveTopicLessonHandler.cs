using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.RemoveTopicLesson;

public sealed class RemoveTopicLessonHandler(StudentsDbContext db) : ICommandHandler<RemoveTopicLesson>
{
    public async Task HandleAsync(RemoveTopicLesson command, CancellationToken ct = default)
    {
        var strand = await db.TopicStrands.FindAsync(new object[] { command.Id }, ct);
        if (strand == null) throw new KeyNotFoundException($"Topic Lesson {command.Id} not found.");

        db.TopicStrands.Remove(strand);
        await db.SaveChangesAsync(ct);
    }
}
