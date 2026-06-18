using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface IStudentSubjectAssignmentRepository
{
    Task<StudentSubjectAssignment?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(StudentSubjectAssignment assignment, CancellationToken cancellationToken = default);
    Task DeleteAsync(StudentSubjectAssignment assignment, CancellationToken cancellationToken = default);
    Task<StudentSubjectAssignmentDto[]> ListByStudentAsync(Guid studentId, Guid periodId, CancellationToken cancellationToken = default);
    Task<StudentSubjectAssignmentDto[]> ListByPeriodAsync(Guid periodId, CancellationToken cancellationToken = default);
}