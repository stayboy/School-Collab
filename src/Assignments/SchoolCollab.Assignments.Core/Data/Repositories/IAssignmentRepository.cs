using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

public interface IAssignmentRepository
{
    Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Assignment assignment, CancellationToken ct = default);
    Task UpdateAsync(Assignment assignment, CancellationToken ct = default);
    Task DeleteAsync(Assignment assignment, CancellationToken ct = default);
    Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? status, CancellationToken ct = default);
    /// <summary>Force the EF change tracker to detect mutations on field-backed
    /// owned-type collections before SaveChanges. Required by the update
    /// handler after a full-replacement of questions/attachments (the
    /// AssignmentConfiguration uses PropertyAccessMode.Field on those
    /// navigations, and neither the InMemory provider nor post-replacement
    /// reference checks pick up the field-level list mutations automatically).</summary>
    void DetectChanges();
}
