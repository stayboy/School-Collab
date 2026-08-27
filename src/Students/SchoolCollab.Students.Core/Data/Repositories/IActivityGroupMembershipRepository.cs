using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface IActivityGroupMembershipRepository
{
    Task<ActivityGroupMembership?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(ActivityGroupMembership membership, CancellationToken cancellationToken = default);
    Task UpdateAsync(ActivityGroupMembership membership, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists all pending tracked changes (e.g. exit mutations during rollover)
    /// in a single <c>SaveChanges</c> — used to batch what would otherwise be one
    /// round-trip per member (FR-50).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a batch of new memberships and persists them in a single
    /// <c>SaveChanges</c> — the second save of the rollover exit-before-create
    /// sequence (FR-50/51).
    /// </summary>
    Task AddRangeAsync(IEnumerable<ActivityGroupMembership> memberships, CancellationToken cancellationToken = default);

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
    /// Returns the active membership <b>entities</b> of a group — used by the
    /// rollover command to exit and re-enroll members (FR-50). Tracked.
    /// </summary>
    Task<ActivityGroupMembership[]> ListActiveAsync(Guid activityGroupId, CancellationToken cancellationToken = default);

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
