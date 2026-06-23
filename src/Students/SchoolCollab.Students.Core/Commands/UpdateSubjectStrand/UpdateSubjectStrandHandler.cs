using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Commands.UpdateSubjectStrand;

public sealed class UpdateSubjectStrandHandler(StudentsDbContext db) : ICommandHandler<UpdateSubjectStrand, SubjectStrandDto>
{
    public async Task<SubjectStrandDto> HandleAsync(UpdateSubjectStrand command, CancellationToken ct = default)
    {
        var strand = await db.SubjectStrands.FindAsync(new object[] { command.Id }, ct);
        if (strand == null) throw new KeyNotFoundException($"Subject Strand {command.Id} not found.");

        strand.Update(command.Name, command.Description, command.DisplayOrder);
        await db.SaveChangesAsync(ct);

        return new SubjectStrandDto(
            strand.Id,
            strand.SubjectId,
            strand.Name,
            strand.Description,
            strand.DisplayOrder,
            strand.CreatedAt,
            strand.UpdatedAt);
    }
}