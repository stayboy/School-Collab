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
                x.Id, x.StudentId, x.PeriodId, x.GradeLevelId, x.StreamCodedValueId,
                x.EnrolledOn, x.ExitDate, x.Status.ToString(),
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<StudentEnrollmentDto[]> ListByStudentAsync(Guid studentId, CancellationToken cancellationToken = default) =>
        await Db.StudentEnrollments
            .AsNoTracking()
            .Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.EnrolledOn)
            .Select(x => new StudentEnrollmentDto(
                x.Id, x.StudentId, x.PeriodId, x.GradeLevelId, x.StreamCodedValueId,
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

    public Task<StudentEnrollment?> GetActiveEnrollmentByStudentAndPeriodAsync(
        Guid studentId, Guid periodId, CancellationToken cancellationToken = default) =>
        Db.StudentEnrollments.FirstOrDefaultAsync(
            x => x.StudentId == studentId && x.PeriodId == periodId && x.Status == EnrollmentStatus.Active,
            cancellationToken);

    public async Task<StudentEnrollment> AddOrReuseAsync(StudentEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        try
        {
            await AddAsync(enrollment, cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException ex) when (IsTenantStudentPeriodUniqueConflict(ex))
        {
            // A concurrent enroll inserted the (tenant, student, period) row
            // first — reuse the winner's row. IMPORTANT: the losing insert is
            // STILL TRACKED as Added (SaveChanges failed but the change tracker
            // keeps the entity), so it must be evicted here or any subsequent
            // SaveChanges in this command re-submits it and fails on the same
            // constraint.
            Db.Entry(enrollment).State = EntityState.Detached;

            return await Db.StudentEnrollments.FirstOrDefaultAsync(
                       x => x.StudentId == enrollment.StudentId && x.PeriodId == enrollment.PeriodId,
                       cancellationToken)
                   ?? throw new InvalidOperationException(
                       $"Unique-constraint conflict on ix_student_enrollments_tenant_student_period " +
                       $"for student {enrollment.StudentId} / period {enrollment.PeriodId}, " +
                       "but no winning row was found afterwards.", ex);
        }
    }

    /// <summary>True when the exception is the Postgres unique violation raised by
    /// <c>ix_student_enrollments_tenant_student_period</c> (SQLSTATE 23505).
    /// Scoped to this index so unrelated constraint failures still surface.</summary>
    private static bool IsTenantStudentPeriodUniqueConflict(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" } pg &&
        pg.ConstraintName?.Contains("tenant_student_period", StringComparison.OrdinalIgnoreCase) == true;
}
