using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

public interface IAssignmentRepository
{
    Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Assignment assignment, CancellationToken ct = default);
    Task UpdateAsync(Assignment assignment, CancellationToken ct = default);
    Task DeleteAsync(Assignment assignment, CancellationToken ct = default);
    Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? status, CancellationToken ct = default);
}
