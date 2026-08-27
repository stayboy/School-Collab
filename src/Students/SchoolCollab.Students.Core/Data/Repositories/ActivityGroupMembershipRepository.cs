using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class ActivityGroupMembershipRepository(StudentsDbContext db)
    : RepositoryBase<ActivityGroupMembership, StudentsDbContext>(db), IActivityGroupMembershipRepository
{
    public override async Task UpdateAsync(ActivityGroupMembership membership, CancellationToken cancellationToken = default)
    {
        try
        {
            await Db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(membership.Id);
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Db.SaveChangesAsync(cancellationToken);

    public async Task AddRangeAsync(IEnumerable<ActivityGroupMembership> memberships, CancellationToken cancellationToken = default)
    {
        var items = memberships as ActivityGroupMembership[] ?? memberships.ToArray();
        Db.ActivityGroupMemberships.AddRange(items);
        await Db.SaveChangesAsync(cancellationToken);
    }

    public Task<ActivityGroupMembership?> GetActiveAsync(Guid studentId, Guid activityGroupId, CancellationToken cancellationToken = default) =>
        Db.ActivityGroupMemberships
            .FirstOrDefaultAsync(m => m.StudentId == studentId
                && m.ActivityGroupId == activityGroupId
                && m.Status == MembershipStatus.Active, cancellationToken);

    public Task<ActivityGroupMembership[]> ListActiveAsync(Guid activityGroupId, CancellationToken cancellationToken = default) =>
        Db.ActivityGroupMemberships
            .Where(m => m.ActivityGroupId == activityGroupId && m.Status == MembershipStatus.Active)
            .ToArrayAsync(cancellationToken);

    public async Task<MembershipDto[]> ListByGroupAsync(Guid activityGroupId, CancellationToken cancellationToken = default) =>
        await Db.ActivityGroupMemberships
            .AsNoTracking()
            .Where(m => m.ActivityGroupId == activityGroupId)
            .OrderByDescending(m => m.JoinedOn)
            .Join(Db.Students, m => m.StudentId, s => s.Id, (m, s) => new MembershipDto(
                m.Id, m.ActivityGroupId, m.StudentId,
                (s.FirstName + " " + s.LastName).Trim(),
                m.PeriodId, m.AutoRenew, m.WindowStartDate, m.WindowEndDate,
                m.JoinedOn, m.ExitedOn, m.Status.ToString(),
                m.CreatedAt, m.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<MembershipDto[]> ListByStudentAsync(Guid studentId, CancellationToken cancellationToken = default) =>
        await Db.ActivityGroupMemberships
            .AsNoTracking()
            .Where(m => m.StudentId == studentId)
            .OrderByDescending(m => m.JoinedOn)
            .Join(Db.Students, m => m.StudentId, s => s.Id, (m, s) => new MembershipDto(
                m.Id, m.ActivityGroupId, m.StudentId,
                (s.FirstName + " " + s.LastName).Trim(),
                m.PeriodId, m.AutoRenew, m.WindowStartDate, m.WindowEndDate,
                m.JoinedOn, m.ExitedOn, m.Status.ToString(),
                m.CreatedAt, m.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<Guid[]> GetActiveMemberStudentIdsAsync(Guid activityGroupId, CancellationToken cancellationToken = default) =>
        await Db.ActivityGroupMemberships
            .AsNoTracking()
            .Where(m => m.ActivityGroupId == activityGroupId && m.Status == MembershipStatus.Active)
            .Select(m => m.StudentId)
            .ToArrayAsync(cancellationToken);
}
