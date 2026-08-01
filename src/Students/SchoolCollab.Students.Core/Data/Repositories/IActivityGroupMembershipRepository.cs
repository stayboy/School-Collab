using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface IActivityGroupMembershipRepository
{
    Task<ActivityGroupMembership?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(ActivityGroupMembership membership, CancellationToken cancellationToken = default);
    Task UpdateAsync(ActivityGroupMembership membership, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active membership for (student, group), or null — used to
    /// detect duplicate-active before insert (FR-10).
    /// </summary>
    Task<ActivityGroupMembership?> GetActiveAsync(Guid studentId, Guid activityGroupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all members of a group (all statuses), ordered by joined date.
    /// </summary>
    Task<MembershipDto[]> ListByGroupAsync(Guid activityGroupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a student's memberships (all statuses), newest first — backs the
    /// student Detail "Activity Groups" section (FR-28).
    /// </summary>
    Task<MembershipDto[]> ListByStudentAsync(Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active member student ids for a set of groups — used by
    /// publish recipient resolution for SelectedGroups (FR-20).
    /// </summary>
    Task<Guid[]> GetActiveMemberStudentIdsAsync(Guid activityGroupId, CancellationToken cancellationToken = default);
}
