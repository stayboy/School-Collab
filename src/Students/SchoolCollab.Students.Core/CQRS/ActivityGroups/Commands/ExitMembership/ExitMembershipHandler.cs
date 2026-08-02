using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.ExitMembership;

public sealed class ExitMembershipHandler(
    IActivityGroupMembershipRepository membershipRepository,
    HybridCache cache,
    ILogger<ExitMembershipHandler> logger) : ICommandHandler<ExitMembership>
{
    public async Task HandleAsync(ExitMembership command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ExitMembership student={StudentId} group={GroupId}",
            command.StudentId, command.ActivityGroupId);

        // FR-14: member voluntarily exits — only an active member can exit.
        var membership = await membershipRepository.GetActiveAsync(
            command.StudentId, command.ActivityGroupId, cancellationToken)
            ?? throw new MembershipNotFoundException(command.ActivityGroupId, command.StudentId);

        membership.Exit();

        await membershipRepository.UpdateAsync(membership, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        membership.ClearDomainEvents();

        logger.LogInformation("Membership {Id} exited: student={StudentId} group={GroupId}",
            membership.Id, command.StudentId, command.ActivityGroupId);
    }
}
