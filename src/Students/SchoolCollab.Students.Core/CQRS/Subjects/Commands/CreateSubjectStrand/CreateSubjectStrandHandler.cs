using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubjectStrand;

public sealed class CreateSubjectStrandHandler(StudentsDbContext db) : ICommandHandler<CreateSubjectStrand, SubjectStrandDto>
{
    public async Task<SubjectStrandDto> HandleAsync(CreateSubjectStrand command, CancellationToken ct = default)
    {
        var strand = SubjectStrand.Create(
            command.SubjectId,
            command.Name,
            command.Description,
            command.DisplayOrder);

        db.SubjectStrands.Add(strand);
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