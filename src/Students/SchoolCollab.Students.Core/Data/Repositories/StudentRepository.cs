using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class StudentRepository(StudentsDbContext db)
    : SoftDeletableRepositoryBase<Student, StudentsDbContext>(db), IStudentRepository
{
    public Task<Student?> GetByStudentNumberAsync(string studentNumber, CancellationToken cancellationToken = default)
    {
        var normalized = studentNumber.Trim().ToUpperInvariant();
        return Db.Students
            .FirstOrDefaultAsync(x => x.StudentNumber == normalized, cancellationToken);
    }

    public Task<bool> ExistsByStudentNumberAsync(string studentNumber, CancellationToken cancellationToken = default)
    {
        var normalized = studentNumber.Trim().ToUpperInvariant();
        return Db.Students
            .AnyAsync(x => x.StudentNumber == normalized, cancellationToken);
    }

    public override async Task UpdateAsync(Student student, CancellationToken cancellationToken = default)
    {
        try
        {
            await Db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(student.Id);
        }
    }

    public async Task<StudentDto[]> ListAsync(CancellationToken cancellationToken = default)
    {
        return await Db.Students
            .AsNoTracking()
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Select(x => new StudentDto(
                x.Id, x.StudentNumber, x.FirstName, x.LastName,
                x.DateOfBirth, x.GenderCodedValueId,
                x.IsDeleted, x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<StudentDto[]> ListDeletedAsync(CancellationToken cancellationToken = default)
    {
        return await DeletedQuery
            .AsNoTracking()
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Select(x => new StudentDto(
                x.Id, x.StudentNumber, x.FirstName, x.LastName,
                x.DateOfBirth, x.GenderCodedValueId,
                x.IsDeleted, x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
    }
}
