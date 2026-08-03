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
        var strand = TopicStrand.Create(
            command.TopicId,
            command.Name,
            command.Description,
            command.DisplayOrder);

        db.TopicStrands.Add(strand);
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