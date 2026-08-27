using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.ActivateActivityGroup;

public sealed class ActivateActivityGroupHandler(
    IActivityGroupRepository repository,
    HybridCache cache,
    ILogger<ActivateActivityGroupHandler> logger) : ICommandHandler<ActivateActivityGroup>
{
    public async Task HandleAsync(ActivateActivityGroup command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ActivateActivityGroup {Id}", command.Id);

        var group = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new ActivityGroupNotFoundException(command.Id);

        group.Activate();

        await repository.UpdateAsync(group, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        group.ClearDomainEvents();

        logger.LogInformation("ActivityGroup {Id} activated", group.Id);
    }
}