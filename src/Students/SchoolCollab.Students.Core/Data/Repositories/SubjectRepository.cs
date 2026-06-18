using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class SubjectRepository(StudentsDbContext db) : ISubjectRepository
{
    public Task<Subject?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Subjects.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Subject?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        db.Subjects.FirstOrDefaultAsync(x => x.Code == code.Trim().ToUpperInvariant(), cancellationToken);

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        db.Subjects.AnyAsync(x => x.Code == code.Trim().ToUpperInvariant(), cancellationToken);

    public async Task AddAsync(Subject subject, CancellationToken cancellationToken = default)
    {
        await db.Subjects.AddAsync(subject, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Subject subject, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(subject.Id);
        }
    }

    public async Task<SubjectDto[]> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Subjects
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new SubjectDto(
                x.Id, x.CodedValueId, x.Code, x.Name, x.DisplayOrder,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
}