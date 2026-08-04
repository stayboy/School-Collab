using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class GradeLevelRepository(StudentsDbContext db)
    : RepositoryBase<GradeLevel, StudentsDbContext>(db), IGradeLevelRepository
{
    public override async Task UpdateAsync(GradeLevel gradeLevel, CancellationToken cancellationToken = default)
    {
        try
        {
            await Db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(gradeLevel.Id);
        }
    }

    public async Task<GradeLevelDto[]> ListAsync(CancellationToken cancellationToken = default) =>
        await Db.GradeLevels
            .AsNoTracking()
            .OrderBy(x => x.Level)
            .Select(x => new GradeLevelDto(
                x.Id, x.CodedValueId, x.Level, x.Name, x.DisplayOrder,
                0, 0,
                x.CreatedAt, x.UpdatedAt,
                x.MinAge, x.MaxAge, x.AllowedGenderCodedValueId, x.IsBlockedFromEnrollment))
            .ToArrayAsync(cancellationToken);

    public Task<GradeLevel?> GetByCodedValueIdAsync(Guid codedValueId, CancellationToken cancellationToken = default)
        => Db.GradeLevels
            .FirstOrDefaultAsync(x => x.CodedValueId == codedValueId, cancellationToken);
}
