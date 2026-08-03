using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.CreateActivityGroup;

public sealed class CreateActivityGroupHandler(
    IActivityGroupRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<CreateActivityGroupHandler> logger) : ICommandHandler<CreateActivityGroup, Guid>
{
    public async Task<Guid> HandleAsync(CreateActivityGroup command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(CreateActivityGroup), typeof(ActivityGroup));

        logger.LogDebug("Handling CreateActivityGroup {Name}", command.Name);

        var group = ActivityGroup.Create(
            command.Name, command.Description, command.Category,
            command.PeriodId, command.Capacity)
            .WithTenant(tenantProvider);

        await repository.AddAsync(group, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        group.ClearDomainEvents();

        logger.LogInformation("ActivityGroup {Id} created with name {Name}", group.Id, group.Name);
        return group.Id;
    }
}
