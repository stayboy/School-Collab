using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentGroups;

public sealed class ListAssignmentGroupsHandler(
    IAssignmentActivityGroupRepository linkRepository,
    IActivityGroupLookup groupLookup) : IQueryHandler<ListAssignmentGroups, ActivityGroupRefDto[]>
{
    public async Task<ActivityGroupRefDto[]> HandleAsync(
        ListAssignmentGroups query, CancellationToken cancellationToken = default)
    {
        var groupIds = await linkRepository.GetGroupIdsForAssignmentAsync(query.AssignmentId, cancellationToken);
        if (groupIds.Length == 0)
            return [];

        // The lookup is tenant-scoped; groups archived after linking are still
        // returned (with their current status) so callers see them.
        return await groupLookup.GetByIdsAsync(groupIds, cancellationToken);
    }
}
