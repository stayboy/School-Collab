using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.DeleteContact;

public sealed class DeleteContactHandler(
    IContactRepository repository,
    HybridCache cache,
    ILogger<DeleteContactHandler> logger) : ICommandHandler<DeleteContact>
{
    public async Task HandleAsync(DeleteContact command, CancellationToken cancellationToken = default)
    {
        var contact = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new ContactNotFoundException(command.Id);

        contact.SoftDelete();

        try
        {
            await repository.UpdateAsync(contact, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Contact", contact.Id);
        }

        await cache.RemoveByTagAsync("contacts", cancellationToken);
        logger.LogInformation("Contact {Id} soft-deleted", contact.Id);
    }
}
