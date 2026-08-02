using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.SuspendActivityGroup;

public sealed class SuspendActivityGroupHandler(
    IActivityGroupRepository repository,
    HybridCache cache,
    ILogger<SuspendActivityGroupHandler> logger) : ICommandHandler<SuspendActivityGroup>
{
    public async Task HandleAsync(SuspendActivityGroup command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling SuspendActivityGroup {Id}", command.Id);

        var group = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new ActivityGroupNotFoundException(command.Id);

        group.Suspend();

        await repository.UpdateAsync(group, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        group.ClearDomainEvents();

        logger.LogInformation("ActivityGroup {Id} suspended", group.Id);
    }
}
