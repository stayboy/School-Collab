using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubjectLesson;

public sealed class CreateSubjectLessonHandler(StudentsDbContext db) : ICommandHandler<CreateSubjectLesson, SubjectLessonDto>
{
    public async Task<SubjectLessonDto> HandleAsync(CreateSubjectLesson command, CancellationToken ct = default)
    {
        var lesson = SubjectLesson.Create(
            command.SubjectId,
            command.Name,
            command.Description,
            command.StartDate,
            command.EndDate,
            command.DisplayOrder);

        db.SubjectLessons.Add(lesson);
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