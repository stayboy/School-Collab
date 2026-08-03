using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface IStudentTopicAssignmentRepository
{
    Task<StudentTopicAssignment?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(StudentTopicAssignment assignment, CancellationToken cancellationToken = default);
    Task DeleteAsync(StudentTopicAssignment assignment, CancellationToken cancellationToken = default);
    Task<StudentTopicAssignmentDto[]> ListByStudentAsync(Guid studentId, Guid periodId, CancellationToken cancellationToken = default);
    Task<StudentTopicAssignmentDto[]> ListByPeriodAsync(Guid periodId, CancellationToken cancellationToken = default);
}