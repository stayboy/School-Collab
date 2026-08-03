using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class ActivityGroupRepository(StudentsDbContext db)
    : RepositoryBase<ActivityGroup, StudentsDbContext>(db), IActivityGroupRepository
{
    public override async Task UpdateAsync(ActivityGroup group, CancellationToken cancellationToken = default)
    {
        try
        {
            await Db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(group.Id);
        }
    }

    public async Task<ActivityGroupDto[]> ListAsync(CancellationToken cancellationToken = default) =>
        await Db.ActivityGroups
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new ActivityGroupDto(
                x.Id, x.Name, x.Description, x.Category, x.PeriodId,
                x.Capacity, x.Status.ToString(),
                Db.ActivityGroupMemberships
                    .Count(m => m.ActivityGroupId == x.Id && m.Status == MembershipStatus.Active),
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public Task<int> CountActiveMembersAsync(Guid activityGroupId, CancellationToken cancellationToken = default) =>
        Db.ActivityGroupMemberships
            .CountAsync(m => m.ActivityGroupId == activityGroupId && m.Status == MembershipStatus.Active, cancellationToken);

    public Task<bool> HasAnyMembershipAsync(Guid activityGroupId, CancellationToken cancellationToken = default) =>
        Db.ActivityGroupMemberships
            .AnyAsync(m => m.ActivityGroupId == activityGroupId, cancellationToken);
}
