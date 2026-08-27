using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.SetMembershipAutoRenew;

public sealed class SetMembershipAutoRenewHandler(
    IActivityGroupMembershipRepository repository,
    HybridCache cache,
    ILogger<SetMembershipAutoRenewHandler> logger) : ICommandHandler<SetMembershipAutoRenew>
{
    public async Task HandleAsync(SetMembershipAutoRenew command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling SetMembershipAutoRenew {Id}", command.MembershipId);

        var membership = await repository.GetAsync(command.MembershipId, cancellationToken)
            ?? throw new MembershipNotFoundException(command.MembershipId);

        membership.SetAutoRenew(command.AutoRenew);

        await repository.UpdateAsync(membership, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("Membership {Id} AutoRenew set to {AutoRenew}", membership.Id, command.AutoRenew);
    }
}