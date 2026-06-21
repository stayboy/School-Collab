using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class StudentRepository(StudentsDbContext db, ITenantProvider tenantProvider) : IStudentRepository
{
    public async Task<Student?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        return await db.Students
            .SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);
    }

    public async Task<Student?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        return await db.Students
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);
    }

    public async Task<Student?> GetByStudentNumberAsync(string studentNumber, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        return await db.Students
            .FirstOrDefaultAsync(x => x.StudentNumber == studentNumber.Trim().ToUpperInvariant() && x.TenantId == tenantId, cancellationToken);
    }

    public async Task<bool> ExistsByStudentNumberAsync(string studentNumber, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        return await db.Students
            .AnyAsync(x => x.StudentNumber == studentNumber.Trim().ToUpperInvariant() && x.TenantId == tenantId, cancellationToken);
    }

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

    public async Task<StudentDto[]> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        return await db.Students
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.TenantId == tenantId)
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Select(x => new StudentDto(
                x.Id, x.StudentNumber, x.FirstName, x.LastName,
                x.DateOfBirth, x.GenderCodedValueId, x.ContactEmail, x.ContactPhone,
                x.IsDeleted, x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<StudentDto[]> ListDeletedAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        return await db.Students
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IsDeleted && x.TenantId == tenantId)
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Select(x => new StudentDto(
                x.Id, x.StudentNumber, x.FirstName, x.LastName,
                x.DateOfBirth, x.GenderCodedValueId, x.ContactEmail, x.ContactPhone,
                x.IsDeleted, x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
    }
}
