using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface ISubjectRepository
{
    Task<Subject?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Subject?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<Subject?> GetByCodedValueIdAsync(Guid codedValueId, CancellationToken cancellationToken = default);
    Task AddAsync(Subject subject, CancellationToken cancellationToken = default);
    Task UpdateAsync(Subject subject, CancellationToken cancellationToken = default);
    Task<SubjectDto[]> ListAsync(CancellationToken cancellationToken = default);
}