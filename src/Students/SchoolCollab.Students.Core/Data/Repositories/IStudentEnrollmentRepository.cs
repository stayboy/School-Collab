using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface IStudentEnrollmentRepository
{
    Task<StudentEnrollment?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(StudentEnrollment enrollment, CancellationToken cancellationToken = default);
    Task UpdateAsync(StudentEnrollment enrollment, CancellationToken cancellationToken = default);
    Task<StudentEnrollmentDto[]> ListByPeriodAsync(Guid periodId, CancellationToken cancellationToken = default);
    Task<StudentEnrollmentDto[]> ListByStudentAsync(Guid studentId, CancellationToken cancellationToken = default);
    Task<StudentEnrollment[]> GetActiveEnrollmentsForPeriodAsync(Guid periodId, CancellationToken cancellationToken = default);
    Task<StudentEnrollment[]> GetActiveEnrollmentsByStudentAsync(Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>The student's ACTIVE enrollment in the given period, or null.
    /// The upsert lookup for the Enroll flow: an enroll submit targeting a
    /// (student, active-period) pair that already has a row must update that
    /// row instead of inserting a second one.</summary>
    Task<StudentEnrollment?> GetActiveEnrollmentByStudentAndPeriodAsync(Guid studentId, Guid periodId, CancellationToken cancellationToken = default);

    /// <summary>Race-safe insert for the enroll path. Inserts the candidate;
    /// if a concurrent request won the unique index
    /// <c>ix_student_enrollments_tenant_student_period</c> (SQLSTATE 23505),
    /// evicts the losing tracked entity and returns the winning row instead
    /// so the caller can converge on it (same pattern as
    /// <c>GradeLevelRepository.AddOrReuseAsync</c>). Unlike
    /// <see cref="GetActiveEnrollmentByStudentAndPeriodAsync"/> the winner is
    /// matched WITHOUT a status filter — the conflicting row may be in any
    /// state, and the caller decides how to converge on it.</summary>
    Task<StudentEnrollment> AddOrReuseAsync(StudentEnrollment enrollment, CancellationToken cancellationToken = default);
}
