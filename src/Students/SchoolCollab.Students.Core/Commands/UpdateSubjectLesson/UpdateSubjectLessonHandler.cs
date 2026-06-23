using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Commands.UpdateSubjectLesson;

public sealed class UpdateSubjectLessonHandler(StudentsDbContext db) : ICommandHandler<UpdateSubjectLesson, SubjectLessonDto>
{
    public async Task<SubjectLessonDto> HandleAsync(UpdateSubjectLesson command, CancellationToken ct = default)
    {
        var lesson = await db.SubjectLessons.FindAsync(new object[] { command.Id }, ct);
        if (lesson == null) throw new KeyNotFoundException($"Subject Lesson {command.Id} not found.");

        lesson.Update(command.Name, command.Description, command.StartDate, command.EndDate, command.DisplayOrder);
        await db.SaveChangesAsync(ct);

        return new SubjectLessonDto(
            lesson.Id,
            lesson.SubjectId,
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