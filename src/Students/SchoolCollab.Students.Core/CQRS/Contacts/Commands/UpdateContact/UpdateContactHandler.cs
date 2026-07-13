using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.UpdateContact;

public sealed class UpdateContactHandler(
    IContactRepository repository,
    HybridCache cache,
    ILogger<UpdateContactHandler> logger) : ICommandHandler<UpdateContact>
{
    public async Task HandleAsync(UpdateContact command, CancellationToken cancellationToken = default)
    {
        var contact = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new ContactNotFoundException(command.Id);

        contact.Update(command.Value, command.Label);

        try
        {
            await repository.UpdateAsync(contact, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Contact", contact.Id);
        }

        await cache.RemoveByTagAsync("contacts", cancellationToken);
        logger.LogInformation("Contact {Id} updated", contact.Id);
    }
}
