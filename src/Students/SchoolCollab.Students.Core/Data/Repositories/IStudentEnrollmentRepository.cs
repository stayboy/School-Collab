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
}