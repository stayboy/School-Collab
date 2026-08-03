using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.RemoveTopicStrand;

public sealed class RemoveTopicStrandHandler(StudentsDbContext db) : ICommandHandler<RemoveTopicStrand>
{
    public async Task HandleAsync(RemoveTopicStrand command, CancellationToken ct = default)
    {
        var strand = await db.TopicStrands.FindAsync(new object[] { command.Id }, ct);
        if (strand == null) throw new KeyNotFoundException($"Topic Strand {command.Id} not found.");

        db.TopicStrands.Remove(strand);
        await db.SaveChangesAsync(ct);
    }
}