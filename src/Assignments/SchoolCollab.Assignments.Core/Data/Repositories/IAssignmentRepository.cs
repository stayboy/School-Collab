using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;

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
    /// <summary>Sanctioned cross-tenant read for the scheduled-publish
    /// sweep (WS-A2 / spec §3.5 step 8). Returns Scheduled assignments
    /// whose <c>AvailableFromUtc</c> has arrived. The dispatch wraps
    /// each candidate in an explicit-tenant context; the read itself
    /// performs no writes.</summary>
    Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
    /// <summary>Sanctioned cross-tenant read for the archive sweep
    /// (WS-A2 / spec §7 Q6). Returns Published/Closed assignments whose
    /// <c>DueDate + ArchiveGraceDays</c> has passed. Same posture as the
    /// scheduled-publish query.</summary>
    Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
}
