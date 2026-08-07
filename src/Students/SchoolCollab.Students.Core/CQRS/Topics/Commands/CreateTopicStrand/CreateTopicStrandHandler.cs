using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopicStrand;

public sealed class CreateTopicStrandHandler(StudentsDbContext db) : ICommandHandler<CreateTopicStrand, TopicStrandDto>
{
    public async Task<TopicStrandDto> HandleAsync(CreateTopicStrand command, CancellationToken ct = default)
    {
        // A strand with a parent is a lesson; validate the parent (root, same topic, not self).
        if (command.ParentStrandId is { } parentId)
        {
            await StrandParentGuard.EnsureValidParentAsync(db, parentId, command.TopicId, strandId: null, ct);
        }

        var strand = TopicStrand.Create(
            command.TopicId,
            command.Name,
            command.Description,
            command.DisplayOrder,
            command.ParentStrandId,
            command.StartDate,
            command.EndDate);

        db.TopicStrands.Add(strand);
        await db.SaveChangesAsync(ct);

        return TopicStrandDto.FromStrand(strand);
    }
}
