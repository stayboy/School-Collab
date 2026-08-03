using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.RemoveMembership;

public sealed class RemoveMembershipHandler(
    IActivityGroupMembershipRepository membershipRepository,
    HybridCache cache,
    ILogger<RemoveMembershipHandler> logger) : ICommandHandler<RemoveMembership>
{
    public async Task HandleAsync(RemoveMembership command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling RemoveMembership student={StudentId} group={GroupId}",
            command.StudentId, command.ActivityGroupId);

        // FR-14: admin removes a member — only an active member can be removed.
        var membership = await membershipRepository.GetActiveAsync(
            command.StudentId, command.ActivityGroupId, cancellationToken)
            ?? throw new MembershipNotFoundException(command.ActivityGroupId, command.StudentId);

        membership.Remove();

        await membershipRepository.UpdateAsync(membership, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        membership.ClearDomainEvents();

        logger.LogInformation("Membership {Id} removed: student={StudentId} group={GroupId}",
            membership.Id, command.StudentId, command.ActivityGroupId);
    }
}
