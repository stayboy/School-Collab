using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface IStudentRepository
{
    Task<Student?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Student?> GetByStudentNumberAsync(string studentNumber, CancellationToken cancellationToken = default);
    Task<bool> ExistsByStudentNumberAsync(string studentNumber, CancellationToken cancellationToken = default);
    Task AddAsync(Student student, CancellationToken cancellationToken = default);
    Task UpdateAsync(Student student, CancellationToken cancellationToken = default);
    Task<StudentDto[]> ListAsync(CancellationToken cancellationToken = default);
    Task<StudentDto[]> ListDeletedAsync(CancellationToken cancellationToken = default);
}