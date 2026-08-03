using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

public interface IAssignmentActivityGroupRepository
{
    /// <summary>
    /// Returns the activity group ids linked to an assignment (FR-18).
    /// </summary>
    Task<Guid[]> GetGroupIdsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default);

    /// <summary>
    /// Replaces the link set for an assignment with the given group ids
    /// (replace-set semantics, FR-17 / §7.3). Removes existing links and adds
    /// the new ones in a single SaveChanges.
    /// </summary>
    Task ReplaceForAssignmentAsync(
        Guid assignmentId, Guid tenantId, IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default);

    /// <summary>
    /// Returns the assignment ids that reference the given group — the reverse
    /// lookup backing <c>GET /api/activity-groups/{id}/assignments</c> and the
    /// FR-6 delete guard.
    /// </summary>
    Task<Guid[]> GetAssignmentIdsByGroupAsync(Guid activityGroupId, CancellationToken ct = default);

    /// <summary>
    /// Returns the assignments referencing the given group as summaries
    /// (<c>AssignmentGroupSummaryDto</c>), newest first.
    /// </summary>
    Task<AssignmentGroupSummaryDto[]> GetAssignmentsByGroupAsync(Guid activityGroupId, CancellationToken ct = default);
}
