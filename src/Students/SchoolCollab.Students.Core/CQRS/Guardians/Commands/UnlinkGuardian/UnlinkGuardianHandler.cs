using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Commands.UnlinkGuardian;

public sealed class UnlinkGuardianHandler(
    IGuardianRepository repository,
    HybridCache cache,
    ILogger<UnlinkGuardianHandler> logger) : ICommandHandler<UnlinkGuardian>
{
    public async Task HandleAsync(UnlinkGuardian command, CancellationToken cancellationToken = default)
    {
        var link = await repository.GetLinkAsync(command.StudentId, command.GuardianId, cancellationToken)
            ?? throw new GuardianLinkNotFoundException(command.StudentId, command.GuardianId);

        await repository.RemoveLinkAsync(link, cancellationToken);
        await cache.RemoveByTagAsync("guardians", cancellationToken);

        logger.LogInformation("Unlinked student {StudentId} from guardian {GuardianId}", command.StudentId, command.GuardianId);
    }
}
