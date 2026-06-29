using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.UpdatePeriod;

public sealed class UpdatePeriodHandler(
    IPeriodRepository repository,
    HybridCache cache,
    ILogger<UpdatePeriodHandler> logger) : ICommandHandler<UpdatePeriod>
{
    public async Task HandleAsync(UpdatePeriod command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling UpdatePeriod {Id}", command.Id);

        var period = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new PeriodNotFoundException(command.Id);

        period.Update(command.Name, command.StartDate, command.EndDate, command.AllowSubjectOverrides);

        try
        {
            await repository.UpdateAsync(period, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Period", period.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        period.ClearDomainEvents();

        logger.LogInformation("Period {Id} updated", period.Id);
    }
}