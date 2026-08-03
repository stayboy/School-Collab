using SchoolCollab.Assignments.Core.DTOs;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Cross-context port (Assignments → Students) used by the group-link command and
/// the SelectedGroups publish path. Validates that activity groups exist in the
/// caller's tenant and are not archived (FR-21, FR-22, EC-11), and resolves the
/// active member roster for publish recipient resolution (FR-20, EC-4).
/// The HTTP client implementation lives in <c>SchoolCollab.Assignments.Api</c> and
/// calls the Students API via Aspire service discovery.
/// </summary>
public interface IActivityGroupLookup
{
    /// <summary>
    /// Returns the resolved groups for the given ids, or only the subset that
    /// exists in the caller's tenant. Missing / cross-tenant groups are omitted —
    /// callers treat omitted ids as rejected (FR-21, EC-11).
    /// </summary>
    Task<ActivityGroupRefDto[]> GetByIdsAsync(
        IReadOnlyList<Guid> activityGroupIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct active-member student ids of the given groups,
    /// excluding groups whose status is <c>Archived</c> (EC-4). Empty when no
    /// group has active members (EC-9).
    /// </summary>
    Task<Guid[]> GetActiveMemberIdsAsync(
        IReadOnlyList<Guid> activityGroupIds, CancellationToken cancellationToken = default);
}
