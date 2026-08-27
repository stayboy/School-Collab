using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Core.Tenancy;
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
                x.Id, x.Name, x.Description, x.Category, x.Capacity, x.IsActive,
                x.Span.ToString(), x.EnrollmentStartDate, x.EnrollmentEndDate, x.AutoRenewDefault,
                Db.ActivityGroupGradeLevels
                    .Where(g => g.ActivityGroupId == x.Id)
                    .Select(g => g.GradeLevelId)
                    .ToArray(),
                Db.ActivityGroupMemberships
                    .Count(m => m.ActivityGroupId == x.Id && m.Status == MembershipStatus.Active),
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<ActivityGroupDto?> GetDtoAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Db.ActivityGroups
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ActivityGroupDto(
                x.Id, x.Name, x.Description, x.Category, x.Capacity, x.IsActive,
                x.Span.ToString(), x.EnrollmentStartDate, x.EnrollmentEndDate, x.AutoRenewDefault,
                Db.ActivityGroupGradeLevels
                    .Where(g => g.ActivityGroupId == x.Id)
                    .Select(g => g.GradeLevelId)
                    .ToArray(),
                Db.ActivityGroupMemberships
                    .Count(m => m.ActivityGroupId == x.Id && m.Status == MembershipStatus.Active),
                x.CreatedAt, x.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<Guid[]> GetEligibleGradeIdsAsync(Guid activityGroupId, CancellationToken cancellationToken = default) =>
        Db.ActivityGroupGradeLevels
            .Where(g => g.ActivityGroupId == activityGroupId)
            .Select(g => g.GradeLevelId)
            .ToArrayAsync(cancellationToken);

    public async Task SetEligibleGradesAsync(Guid activityGroupId, IEnumerable<Guid> gradeLevelIds, CancellationToken cancellationToken = default)
    {
        var ids = (gradeLevelIds ?? []).Distinct().ToArray();

        var existing = await Db.ActivityGroupGradeLevels
            .Where(g => g.ActivityGroupId == activityGroupId)
            .ToListAsync(cancellationToken);
        Db.ActivityGroupGradeLevels.RemoveRange(existing);

        foreach (var gradeLevelId in ids)
        {
            var link = ActivityGroupGradeLevel.Create(activityGroupId, gradeLevelId);
            ((ITenantEntity)link).TenantId = Db.CurrentTenantId;
            Db.ActivityGroupGradeLevels.Add(link);
        }

        await Db.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountActiveMembersAsync(Guid activityGroupId, Guid? periodId = null, CancellationToken cancellationToken = default) =>
        Db.ActivityGroupMemberships
            .CountAsync(m => m.ActivityGroupId == activityGroupId
                && m.Status == MembershipStatus.Active
                && (periodId == null || m.PeriodId == periodId), cancellationToken);

    public Task<bool> HasAnyMembershipAsync(Guid activityGroupId, CancellationToken cancellationToken = default) =>
        Db.ActivityGroupMemberships
            .AnyAsync(m => m.ActivityGroupId == activityGroupId, cancellationToken);

    public Task<Guid[]> GetGroupsDueForRolloverAsync(DateOnly today, CancellationToken cancellationToken = default) =>
        Db.ActivityGroups
            .Where(g => g.Span == EnrollmentSpan.DateRange
                && g.EnrollmentEndDate != null
                && g.EnrollmentEndDate < today)
            .Select(g => g.Id)
            .ToArrayAsync(cancellationToken);
}
