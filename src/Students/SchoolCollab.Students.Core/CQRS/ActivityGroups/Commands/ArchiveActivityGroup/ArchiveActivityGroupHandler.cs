using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.ArchiveActivityGroup;

public sealed class ArchiveActivityGroupHandler(
    IActivityGroupRepository repository,
    HybridCache cache,
    ILogger<ArchiveActivityGroupHandler> logger) : ICommandHandler<ArchiveActivityGroup>
{
    public async Task HandleAsync(ArchiveActivityGroup command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ArchiveActivityGroup {Id}", command.Id);

        var group = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new ActivityGroupNotFoundException(command.Id);

        group.Archive();

        await repository.UpdateAsync(group, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        group.ClearDomainEvents();

        logger.LogInformation("ActivityGroup {Id} archived", group.Id);
    }
}
