using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class StudentRepository(StudentsDbContext db) : IStudentRepository
{
    public Task<Student?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Students.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Student?> GetByStudentNumberAsync(string studentNumber, CancellationToken cancellationToken = default) =>
        db.Students.FirstOrDefaultAsync(x => x.StudentNumber == studentNumber.Trim().ToUpperInvariant(), cancellationToken);

    public Task<bool> ExistsByStudentNumberAsync(string studentNumber, CancellationToken cancellationToken = default) =>
        db.Students.AnyAsync(x => x.StudentNumber == studentNumber.Trim().ToUpperInvariant(), cancellationToken);

    public async Task AddAsync(Student student, CancellationToken cancellationToken = default)
    {
        await db.Students.AddAsync(student, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Student student, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(student.Id);
        }
    }

    public async Task<StudentDto[]> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Students
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Select(x => new StudentDto(
                x.Id, x.StudentNumber, x.FirstName, x.LastName,
                x.DateOfBirth, x.GenderCodedValueId, x.ContactEmail, x.ContactPhone,
                x.IsDeleted, x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<StudentDto[]> ListDeletedAsync(CancellationToken cancellationToken = default) =>
        await db.Students
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IsDeleted)
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Select(x => new StudentDto(
                x.Id, x.StudentNumber, x.FirstName, x.LastName,
                x.DateOfBirth, x.GenderCodedValueId, x.ContactEmail, x.ContactPhone,
                x.IsDeleted, x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
}