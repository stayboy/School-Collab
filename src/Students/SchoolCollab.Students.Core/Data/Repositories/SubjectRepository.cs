using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class SubjectRepository(StudentsDbContext db)
    : RepositoryBase<Subject, StudentsDbContext>(db), ISubjectRepository
{
    public Task<Subject?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return Db.Subjects.FirstOrDefaultAsync(x => x.Code == normalized, cancellationToken);
    }

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return Db.Subjects.AnyAsync(x => x.Code == normalized, cancellationToken);
    }

    public override async Task UpdateAsync(Subject subject, CancellationToken cancellationToken = default)
    {
        try
        {
            await Db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(subject.Id);
        }
    }

    public async Task<SubjectDto[]> ListAsync(CancellationToken cancellationToken = default) =>
        await Db.Subjects
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new SubjectDto(
                x.Id, x.CodedValueId, x.Code, x.Name, x.DisplayOrder,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
}
