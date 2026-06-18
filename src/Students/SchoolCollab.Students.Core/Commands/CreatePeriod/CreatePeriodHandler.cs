using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Commands.CreatePeriod;

public sealed class CreatePeriodHandler(
    IPeriodRepository repository,
    HybridCache cache,
    ILogger<CreatePeriodHandler> logger) : ICommandHandler<CreatePeriod, Guid>
{
    public async Task<Guid> HandleAsync(CreatePeriod command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling CreatePeriod {Name}", command.Name);

        var period = Period.Create(
            command.Name,
            command.StartDate,
            command.EndDate,
            command.AllowSubjectOverrides);

        await repository.AddAsync(period, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        period.ClearDomainEvents();

        logger.LogInformation("Period {Id} created with name {Name}", period.Id, period.Name);
        return period.Id;
    }
}