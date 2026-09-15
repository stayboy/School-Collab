using SchoolCollab.Assignments.Core.DTOs;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

/// <summary>
/// WS-A5 — dedicated ward-facing aggregation read over the assignment +
/// recipient candidate set. Split out of <see cref="IModuleProgressRepository"/>
/// in slice 2b (ar-13 A-1 cohesion note): this is an assignment/recipient
/// projection, not module progress, and is consumed only by
/// <see cref="ListWardAssignmentsHandler"/>.
/// </summary>
public interface IWardAssignmentProjectionRepository
{
    /// <summary>The ward's visible assignment candidate set: Published (or
    /// Scheduled-active per the existing visibility rule) assignments the
    /// student is already a linked recipient of (recipient-link targeting;
    /// grade/group targeting resolution is cross-context and lands in slice
    /// 2b). Returns <see cref="AssignmentSummary"/> rows the ward-list handler
    /// enriches with module-lock + submission state.</summary>
    Task<List<AssignmentSummary>> ListWardAssignmentsAsync(Guid studentId, DateTimeOffset nowUtc, CancellationToken ct = default);
}
