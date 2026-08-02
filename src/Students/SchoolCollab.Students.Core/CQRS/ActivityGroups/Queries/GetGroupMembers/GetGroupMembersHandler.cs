using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetGroupMembers;

public sealed class GetGroupMembersHandler(IActivityGroupMembershipRepository membershipRepository)
    : IQueryHandler<GetGroupMembers, MembershipDto[]>
{
    public Task<MembershipDto[]> HandleAsync(
        GetGroupMembers query, CancellationToken cancellationToken = default) =>
        membershipRepository.ListByGroupAsync(query.ActivityGroupId, cancellationToken);
}
