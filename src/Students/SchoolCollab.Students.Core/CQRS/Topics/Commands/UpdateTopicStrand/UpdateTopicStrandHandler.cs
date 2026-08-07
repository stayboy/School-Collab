using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.UpdateTopicStrand;

public sealed class UpdateTopicStrandHandler(StudentsDbContext db) : ICommandHandler<UpdateTopicStrand, TopicStrandDto>
{
    public async Task<TopicStrandDto> HandleAsync(UpdateTopicStrand command, CancellationToken ct = default)
    {
        var strand = await db.TopicStrands.FindAsync(new object[] { command.Id }, ct);
        if (strand == null) throw new KeyNotFoundException($"Topic Strand {command.Id} not found.");

        if (command.ParentStrandId is { } parentId)
        {
            await StrandParentGuard.EnsureValidParentAsync(db, parentId, strand.TopicId, strand.Id, ct);
        }

        strand.Update(command.Name, command.Description, command.DisplayOrder, command.ParentStrandId, command.StartDate, command.EndDate);
        await db.SaveChangesAsync(ct);

        return TopicStrandDto.FromStrand(strand);
    }
}
