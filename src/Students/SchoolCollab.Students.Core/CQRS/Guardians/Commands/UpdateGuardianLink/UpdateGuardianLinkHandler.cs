using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Commands.UpdateGuardianLink;

public sealed class UpdateGuardianLinkHandler(
    IGuardianRepository repository,
    IIntegrationEventPublisher publisher,
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

        // Spec §3.2 / §5: emit a single StudentGuardianUpdated integration
        // event via the transactional outbox (no unlink+relink double event).
        foreach (var evt in link.DomainEvents.OfType<StudentGuardianUpdatedEvent>())
        {
            await publisher.EnqueueAsync(new StudentGuardianUpdated(
                evt.StudentId,
                evt.GuardianId,
                evt.Role.ToString(),
                evt.RelationshipCodedValueId,
                evt.IsEmergencyContact,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        link.ClearDomainEvents();

        await cache.RemoveByTagAsync("guardians", cancellationToken);
        logger.LogInformation("Updated link {Id}", link.Id);
    }
}
