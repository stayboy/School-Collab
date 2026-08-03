using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface ITopicRepository
{
    Task<Topic?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Topic?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<Topic?> GetByCodedValueIdAsync(Guid codedValueId, CancellationToken cancellationToken = default);
    Task AddAsync(Topic subject, CancellationToken cancellationToken = default);
    Task UpdateAsync(Topic subject, CancellationToken cancellationToken = default);
    Task<TopicDto[]> ListAsync(CancellationToken cancellationToken = default);
}