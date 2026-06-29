using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.RemoveSubjectLesson;

public sealed class RemoveSubjectLessonHandler(StudentsDbContext db) : ICommandHandler<RemoveSubjectLesson>
{
    public async Task HandleAsync(RemoveSubjectLesson command, CancellationToken ct = default)
    {
        var lesson = await db.SubjectLessons.FindAsync(new object[] { command.Id }, ct);
        if (lesson == null) throw new KeyNotFoundException($"Subject Lesson {command.Id} not found.");

        db.SubjectLessons.Remove(lesson);
        await db.SaveChangesAsync(ct);
    }
}