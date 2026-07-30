using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class StudentEnrollmentRepository(StudentsDbContext db)
    : RepositoryBase<StudentEnrollment, StudentsDbContext>(db), IStudentEnrollmentRepository
{
    public override async Task UpdateAsync(StudentEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        try
        {
            await Db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(enrollment.Id);
        }
    }

    public async Task<StudentEnrollmentDto[]> ListByPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) =>
        await Db.StudentEnrollments
            .AsNoTracking()
            .Where(x => x.PeriodId == periodId)
            .OrderBy(x => x.StudentId)
            .Select(x => new StudentEnrollmentDto(
                x.Id, x.StudentId, x.PeriodId, x.GradeLevelId, x.GradeStrandCodedValueId,
                x.EnrolledOn, x.ExitDate, x.Status.ToString(),
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<StudentEnrollmentDto[]> ListByStudentAsync(Guid studentId, CancellationToken cancellationToken = default) =>
        await Db.StudentEnrollments
            .AsNoTracking()
            .Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.EnrolledOn)
            .Select(x => new StudentEnrollmentDto(
                x.Id, x.StudentId, x.PeriodId, x.GradeLevelId, x.GradeStrandCodedValueId,
                x.EnrolledOn, x.ExitDate, x.Status.ToString(),
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<StudentEnrollment[]> GetActiveEnrollmentsForPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) =>
    await Db.StudentEnrollments
        .Where(x => x.PeriodId == periodId && x.Status == EnrollmentStatus.Active)
        .ToArrayAsync(cancellationToken);

    public async Task<StudentEnrollment[]> GetActiveEnrollmentsByStudentAsync(
        Guid studentId, CancellationToken cancellationToken = default) =>
        await Db.StudentEnrollments
            .Where(x => x.StudentId == studentId && x.Status == EnrollmentStatus.Active)
            .ToArrayAsync(cancellationToken);
}
