using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.SetPrimaryContact;

public sealed class SetPrimaryContactHandler(
    IContactRepository repository,
    HybridCache cache,
    ILogger<SetPrimaryContactHandler> logger) : ICommandHandler<SetPrimaryContact>
{
    public async Task HandleAsync(SetPrimaryContact command, CancellationToken cancellationToken = default)
    {
        var contact = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new ContactNotFoundException(command.Id);

        try
        {
            await repository.SetPrimaryAsync(contact.Id, contact.OwnerType, contact.OwnerId, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Contact", contact.Id);
        }

        await cache.RemoveByTagAsync("contacts", cancellationToken);
        logger.LogInformation("Contact {Id} set as primary", contact.Id);
    }
}
