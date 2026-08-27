using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.DeactivateActivityGroup;

public sealed class DeactivateActivityGroupHandler(
    IActivityGroupRepository repository,
    HybridCache cache,
    ILogger<DeactivateActivityGroupHandler> logger) : ICommandHandler<DeactivateActivityGroup>
{
    public async Task HandleAsync(DeactivateActivityGroup command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling DeactivateActivityGroup {Id}", command.Id);

        var group = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new ActivityGroupNotFoundException(command.Id);

        group.Deactivate();

        await repository.UpdateAsync(group, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        group.ClearDomainEvents();

        logger.LogInformation("ActivityGroup {Id} deactivated", group.Id);
    }
}