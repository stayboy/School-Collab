using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Commands.DeleteGuardian;

public sealed class DeleteGuardianHandler(
    IGuardianRepository repository,
    HybridCache cache,
    ILogger<DeleteGuardianHandler> logger) : ICommandHandler<DeleteGuardian>
{
    public async Task HandleAsync(DeleteGuardian command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling DeleteGuardian {Id}", command.Id);

        var guardian = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new GuardianNotFoundException(command.Id);

        guardian.SoftDelete();

        try
        {
            await repository.UpdateAsync(guardian, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Guardian", guardian.Id);
        }

        await cache.RemoveByTagAsync("guardians", cancellationToken);
        logger.LogInformation("Guardian {Id} soft-deleted", guardian.Id);
    }
}
