using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsForGroup;

public sealed class ListAssignmentsForGroupHandler(
    IAssignmentActivityGroupRepository linkRepository) : IQueryHandler<ListAssignmentsForGroup, AssignmentGroupSummaryDto[]>
{
    public Task<AssignmentGroupSummaryDto[]> HandleAsync(
        ListAssignmentsForGroup query, CancellationToken cancellationToken = default) =>
        linkRepository.GetAssignmentsByGroupAsync(query.ActivityGroupId, cancellationToken);
}
