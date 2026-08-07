using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.AssignLessonStrand;

public sealed class AssignLessonStrandHandler(StudentsDbContext db) : ICommandHandler<AssignLessonStrand, TopicLessonDto>
{
    public async Task<TopicLessonDto> HandleAsync(AssignLessonStrand command, CancellationToken ct = default)
    {
        var strand = await db.TopicStrands.FindAsync(new object[] { command.LessonId }, ct);
        if (strand == null) throw new KeyNotFoundException($"Topic Lesson {command.LessonId} not found.");

        strand.SetParent(command.StrandId);
        await db.SaveChangesAsync(ct);

        return TopicLessonDto.FromStrand(strand);
    }
}
