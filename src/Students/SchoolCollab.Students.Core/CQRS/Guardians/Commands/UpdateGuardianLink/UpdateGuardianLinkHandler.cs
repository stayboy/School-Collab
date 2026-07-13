using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Commands.UpdateGuardianLink;

public sealed class UpdateGuardianLinkHandler(
    IGuardianRepository repository,
    HybridCache cache,
    ILogger<UpdateGuardianLinkHandler> logger) : ICommandHandler<UpdateGuardianLink>
{
    public async Task HandleAsync(UpdateGuardianLink command, CancellationToken cancellationToken = default)
    {
        var link = await repository.GetLinkAsync(command.StudentId, command.GuardianId, cancellationToken)
            ?? throw new GuardianLinkNotFoundException(command.StudentId, command.GuardianId);

        link.Update(command.Role, command.RelationshipCodedValueId, command.IsEmergencyContact);

        try
        {
            await repository.UpdateLinkAsync(link, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("StudentGuardian", link.Id);
        }

        await cache.RemoveByTagAsync("guardians", cancellationToken);
        logger.LogInformation("Updated link {Id}", link.Id);
    }
}
