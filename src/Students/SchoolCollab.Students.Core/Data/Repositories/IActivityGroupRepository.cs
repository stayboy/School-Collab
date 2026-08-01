using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface IActivityGroupRepository
{
    Task<ActivityGroup?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(ActivityGroup group, CancellationToken cancellationToken = default);
    Task UpdateAsync(ActivityGroup group, CancellationToken cancellationToken = default);
    Task DeleteAsync(ActivityGroup group, CancellationToken cancellationToken = default);
    Task<ActivityGroupDto[]> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active member count for the group (FR-13 capacity check).
    /// </summary>
    Task<int> CountActiveMembersAsync(Guid activityGroupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether the group has any membership row (any status) — the
    /// FR-6/NFR-8 delete guard (a group with membership history cannot be
    /// hard-deleted).
    /// </summary>
    Task<bool> HasAnyMembershipAsync(Guid activityGroupId, CancellationToken cancellationToken = default);
}
