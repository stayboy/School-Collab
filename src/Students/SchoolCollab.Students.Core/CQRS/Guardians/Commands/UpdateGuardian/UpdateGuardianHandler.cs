using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Commands.UpdateGuardian;

public sealed class UpdateGuardianHandler(
    IGuardianRepository repository,
    HybridCache cache,
    ILogger<UpdateGuardianHandler> logger) : ICommandHandler<UpdateGuardian>
{
    public async Task HandleAsync(UpdateGuardian command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling UpdateGuardian {Id}", command.Id);

        var guardian = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new GuardianNotFoundException(command.Id);

        guardian.Update(
            command.TitleCodedValueId,
            command.FirstName,
            command.LastName,
            command.DisplayName,
            command.Address,
            command.CommunityId);

        try
        {
            await repository.UpdateAsync(guardian, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Guardian", guardian.Id);
        }

        await repository.PersistNameHistoryAsync(guardian, cancellationToken);
        await cache.RemoveByTagAsync("guardians", cancellationToken);
        logger.LogInformation("Guardian {Id} updated", guardian.Id);
    }
}
