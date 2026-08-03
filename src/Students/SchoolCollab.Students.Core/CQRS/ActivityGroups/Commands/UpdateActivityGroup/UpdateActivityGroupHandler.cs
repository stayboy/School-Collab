using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.UpdateActivityGroup;

public sealed class UpdateActivityGroupHandler(
    IActivityGroupRepository repository,
    HybridCache cache,
    ILogger<UpdateActivityGroupHandler> logger) : ICommandHandler<UpdateActivityGroup>
{
    public async Task HandleAsync(UpdateActivityGroup command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling UpdateActivityGroup {Id}", command.Id);

        var group = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new ActivityGroupNotFoundException(command.Id);

        group.Update(command.Name, command.Description, command.Category,
            command.PeriodId, command.Capacity);

        await repository.UpdateAsync(group, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        group.ClearDomainEvents();

        logger.LogInformation("ActivityGroup {Id} updated", group.Id);
    }
}
