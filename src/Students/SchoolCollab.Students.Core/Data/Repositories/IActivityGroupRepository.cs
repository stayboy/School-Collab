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
    Task<ActivityGroupDto?> GetDtoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the group's eligible grade-level ids (Rev. 2 FR-40). An empty
    /// array means any grade is eligible.
    /// </summary>
    Task<Guid[]> GetEligibleGradeIdsAsync(Guid activityGroupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replace-set semantics for the group's eligible grades (Rev. 2 FR-39/40):
    /// the existing eligible set is replaced with <paramref name="gradeLevelIds"/>.
    /// </summary>
    Task SetEligibleGradesAsync(Guid activityGroupId, IEnumerable<Guid> gradeLevelIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active member count for the group (FR-13/FR-46 capacity
    /// check). When <paramref name="periodId"/> is set, counts active members of
    /// that (group, period) — used for period-aligned spans; when null, counts all
    /// active members of the group overall — used for OpenEnded/DateRange.
    /// </summary>
    Task<int> CountActiveMembersAsync(Guid activityGroupId, Guid? periodId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether the group has any membership row (any status) — the
    /// FR-6/NFR-8 delete guard (a group with membership history cannot be
    /// hard-deleted).
    /// </summary>
    Task<bool> HasAnyMembershipAsync(Guid activityGroupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the ids of DateRange groups whose enrollment window has ended
    /// (<c>EnrollmentEndDate &lt; today</c>) — the scheduled rollover sweep (FR-54).
    /// </summary>
    Task<Guid[]> GetGroupsDueForRolloverAsync(DateOnly today, CancellationToken cancellationToken = default);
}
