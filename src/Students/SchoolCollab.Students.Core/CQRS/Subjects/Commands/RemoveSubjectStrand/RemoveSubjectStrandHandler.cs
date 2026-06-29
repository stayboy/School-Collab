using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.RemoveSubjectStrand;

public sealed class RemoveSubjectStrandHandler(StudentsDbContext db) : ICommandHandler<RemoveSubjectStrand>
{
    public async Task HandleAsync(RemoveSubjectStrand command, CancellationToken ct = default)
    {
        var strand = await db.SubjectStrands.FindAsync(new object[] { command.Id }, ct);
        if (strand == null) throw new KeyNotFoundException($"Subject Strand {command.Id} not found.");

        db.SubjectStrands.Remove(strand);
        await db.SaveChangesAsync(ct);
    }
}