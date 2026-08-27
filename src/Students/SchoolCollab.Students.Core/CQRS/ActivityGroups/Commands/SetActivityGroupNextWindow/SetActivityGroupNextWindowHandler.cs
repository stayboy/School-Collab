using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.SetActivityGroupNextWindow;

public sealed class SetActivityGroupNextWindowHandler(
    IActivityGroupRepository repository,
    HybridCache cache,
    ILogger<SetActivityGroupNextWindowHandler> logger) : ICommandHandler<SetActivityGroupNextWindow>
{
    public async Task HandleAsync(SetActivityGroupNextWindow command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling SetActivityGroupNextWindow {Id}", command.ActivityGroupId);

        var group = await repository.GetAsync(command.ActivityGroupId, cancellationToken)
            ?? throw new ActivityGroupNotFoundException(command.ActivityGroupId);

        group.SetNextWindow(command.NextStartDate, command.NextEndDate);

        await repository.UpdateAsync(group, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        group.ClearDomainEvents();

        logger.LogInformation("ActivityGroup {Id} next window set to {Start}–{End}",
            group.Id, command.NextStartDate, command.NextEndDate);
    }
}