using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.UpdateTopicLesson;

public sealed class UpdateTopicLessonHandler(StudentsDbContext db) : ICommandHandler<UpdateTopicLesson, TopicLessonDto>
{
    public async Task<TopicLessonDto> HandleAsync(UpdateTopicLesson command, CancellationToken ct = default)
    {
        var strand = await db.TopicStrands.FindAsync(new object[] { command.Id }, ct);
        if (strand == null) throw new KeyNotFoundException($"Topic Lesson {command.Id} not found.");

        strand.Update(command.Name, command.Description, command.DisplayOrder, startDate: command.StartDate, endDate: command.EndDate);
        await db.SaveChangesAsync(ct);

        return TopicLessonDto.FromStrand(strand);
    }
}
