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
            .Union(GroupTargetAssignmentIds(activityGroupId))
            .ToArrayAsync(ct);

    public Task<AssignmentGroupSummaryDto[]> GetAssignmentsByGroupAsync(Guid activityGroupId, CancellationToken ct = default) =>
        db.AssignmentActivityGroups
            .AsNoTracking()
            .Where(l => l.ActivityGroupId == activityGroupId)
            .Select(l => l.AssignmentId)
            // D-8.2 / FR-6: a group referenced by an AssignmentTarget row must block the hard
            // delete exactly like a link-table reference, so the guard read unions both.
            .Union(GroupTargetAssignmentIds(activityGroupId))
            .Join(db.Assignments, id => id, a => a.Id, (id, a) => new AssignmentGroupSummaryDto(
                a.Id, a.Title, a.Status.ToString()))
            .OrderByDescending(s => s.Title)
            .ToArrayAsync(ct);

    /// <summary>The assignment ids referencing the group through an
    /// <c>ActivityGroup</c> target row (D-8.2 — the FR-6 delete guard reads both sources).</summary>
    private IQueryable<Guid> GroupTargetAssignmentIds(Guid activityGroupId) =>
        db.AssignmentTargets
            .AsNoTracking()
            .Where(t => t.Kind == TargetKind.ActivityGroup && t.RefId == activityGroupId)
            .Select(t => t.AssignmentId);
}
