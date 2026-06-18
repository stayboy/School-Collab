using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class GradeLevelRepository(StudentsDbContext db) : IGradeLevelRepository
{
    public Task<GradeLevel?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.GradeLevels.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task AddAsync(GradeLevel gradeLevel, CancellationToken cancellationToken = default)
    {
        await db.GradeLevels.AddAsync(gradeLevel, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(GradeLevel gradeLevel, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(gradeLevel.Id);
        }
    }

    public async Task<GradeLevelDto[]> ListAsync(CancellationToken cancellationToken = default) =>
        await db.GradeLevels
            .AsNoTracking()
            .OrderBy(x => x.Level)
            .Select(x => new GradeLevelDto(
                x.Id, x.CodedValueId, x.Level, x.Name, x.DisplayOrder,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
}