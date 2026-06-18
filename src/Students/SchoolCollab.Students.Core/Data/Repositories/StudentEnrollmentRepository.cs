using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class StudentEnrollmentRepository(StudentsDbContext db) : IStudentEnrollmentRepository
{
    public Task<StudentEnrollment?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.StudentEnrollments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task AddAsync(StudentEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        await db.StudentEnrollments.AddAsync(enrollment, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(StudentEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(enrollment.Id);
        }
    }

    public async Task<StudentEnrollmentDto[]> ListByPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) =>
        await db.StudentEnrollments
            .AsNoTracking()
            .Where(x => x.PeriodId == periodId)
            .OrderBy(x => x.StudentId)
            .Select(x => new StudentEnrollmentDto(
                x.Id, x.StudentId, x.PeriodId, x.GradeLevelId,
                x.EnrolledOn, x.ExitDate, x.Status.ToString(),
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<StudentEnrollmentDto[]> ListByStudentAsync(Guid studentId, CancellationToken cancellationToken = default) =>
        await db.StudentEnrollments
            .AsNoTracking()
            .Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.EnrolledOn)
            .Select(x => new StudentEnrollmentDto(
                x.Id, x.StudentId, x.PeriodId, x.GradeLevelId,
                x.EnrolledOn, x.ExitDate, x.Status.ToString(),
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<StudentEnrollment[]> GetActiveEnrollmentsForPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) =>
        await db.StudentEnrollments
            .Where(x => x.PeriodId == periodId && x.Status == EnrollmentStatus.Active)
            .ToArrayAsync(cancellationToken);
}