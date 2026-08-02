using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.Data.Repositories;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

internal sealed class AssignmentActivityGroupRepository(AssignmentsDbContext db)
    : IAssignmentActivityGroupRepository
{
    public Task<Guid[]> GetGroupIdsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) =>
        db.AssignmentActivityGroups
            .AsNoTracking()
            .Where(l => l.AssignmentId == assignmentId)
            .Select(l => l.ActivityGroupId)
            .ToArrayAsync(ct);

    public async Task ReplaceForAssignmentAsync(
        Guid assignmentId, Guid tenantId, IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default)
    {
        var existing = await db.AssignmentActivityGroups
            .Where(l => l.AssignmentId == assignmentId)
            .ToArrayAsync(ct);

        db.AssignmentActivityGroups.RemoveRange(existing);

        foreach (var groupId in activityGroupIds)
            db.AssignmentActivityGroups.Add(
                AssignmentActivityGroup.Create(tenantId, assignmentId, groupId));

        await db.SaveChangesAsync(ct);
    }

    public Task<Guid[]> GetAssignmentIdsByGroupAsync(Guid activityGroupId, CancellationToken ct = default) =>
        db.AssignmentActivityGroups
            .AsNoTracking()
            .Where(l => l.ActivityGroupId == activityGroupId)
            .Select(l => l.AssignmentId)
            .ToArrayAsync(ct);

    public Task<AssignmentGroupSummaryDto[]> GetAssignmentsByGroupAsync(Guid activityGroupId, CancellationToken ct = default) =>
        db.AssignmentActivityGroups
            .AsNoTracking()
            .Where(l => l.ActivityGroupId == activityGroupId)
            .Join(db.Assignments, l => l.AssignmentId, a => a.Id, (l, a) => new AssignmentGroupSummaryDto(
                a.Id, a.Title, a.Status.ToString()))
            .OrderByDescending(s => s.Title)
            .ToArrayAsync(ct);
}
