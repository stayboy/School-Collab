using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.AssignLessonStrand;

public sealed class AssignLessonStrandHandler(StudentsDbContext db) : ICommandHandler<AssignLessonStrand, SubjectLessonDto>
{
    public async Task<SubjectLessonDto> HandleAsync(AssignLessonStrand command, CancellationToken ct = default)
    {
        var lesson = await db.SubjectLessons.FindAsync(new object[] { command.LessonId }, ct);
        if (lesson == null) throw new KeyNotFoundException($"Subject Lesson {command.LessonId} not found.");

        lesson.SetStrand(command.StrandId);
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