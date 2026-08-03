using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Services;

/// <summary>
/// Cross-context port (spec activity-group-enrollment.md FR-6 / EC-1) used by
/// <c>DeleteActivityGroupHandler</c> to check whether any assignment in the
/// Assignments bounded context references the activity group being deleted.
/// The HTTP client implementation lives in <c>SchoolCollab.Students.Api</c>
/// and calls <c>GET /api/activity-groups/{id}/assignments</c> on the
/// assignments-api via Aspire service discovery.
/// The check is <b>fail-closed</b>: if the Assignments API is unreachable the
/// delete handler rejects the delete (throws <see cref="ActivityGroupReferencedException"/>).
/// </summary>
public interface IActivityGroupAssignmentQuery
{
    /// <summary>
    /// Returns the assignments that reference the given activity group, or an
    /// empty array if none. Throws if the Assignments API is unreachable
    /// (the caller should fail-closed).
    /// </summary>
    Task<AssignmentReferenceDto[]> GetReferencingAssignmentsAsync(
        Guid activityGroupId, CancellationToken cancellationToken = default);
}
