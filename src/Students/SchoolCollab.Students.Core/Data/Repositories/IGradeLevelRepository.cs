using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface IGradeLevelRepository
{
    Task<GradeLevel?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(GradeLevel gradeLevel, CancellationToken cancellationToken = default);
    Task UpdateAsync(GradeLevel gradeLevel, CancellationToken cancellationToken = default);
    Task<GradeLevelDto[]> ListAsync(CancellationToken cancellationToken = default);
}