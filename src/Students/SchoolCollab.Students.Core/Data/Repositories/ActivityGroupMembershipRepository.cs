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

    public Task<ActivityGroupMembership?> GetActiveAsync(Guid studentId, Guid activityGroupId, CancellationToken cancellationToken = default) =>
        Db.ActivityGroupMemberships
            .FirstOrDefaultAsync(m => m.StudentId == studentId
                && m.ActivityGroupId == activityGroupId
                && m.Status == MembershipStatus.Active, cancellationToken);

    public async Task<MembershipDto[]> ListByGroupAsync(Guid activityGroupId, CancellationToken cancellationToken = default) =>
        await Db.ActivityGroupMemberships
            .AsNoTracking()
            .Where(m => m.ActivityGroupId == activityGroupId)
            .OrderByDescending(m => m.JoinedOn)
            .Select(m => new MembershipDto(
                m.Id, m.ActivityGroupId, m.StudentId, string.Empty,
                m.JoinedOn, m.ExitedOn, m.Status.ToString(),
                m.CreatedAt, m.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<MembershipDto[]> ListByStudentAsync(Guid studentId, CancellationToken cancellationToken = default) =>
        await Db.ActivityGroupMemberships
            .AsNoTracking()
            .Where(m => m.StudentId == studentId)
            .OrderByDescending(m => m.JoinedOn)
            .Select(m => new MembershipDto(
                m.Id, m.ActivityGroupId, m.StudentId, string.Empty,
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
