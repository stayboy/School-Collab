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

        strand.Update(command.Name, command.Description, command.DisplayOrder);
        await db.SaveChangesAsync(ct);

        return new TopicStrandDto(
            strand.Id,
            strand.TopicId,
            strand.Name,
            strand.Description,
            strand.DisplayOrder,
            strand.CreatedAt,
            strand.UpdatedAt);
    }
}