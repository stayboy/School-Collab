using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.SetContactOrder;

public sealed class SetContactOrderHandler(
    IContactRepository repository,
    HybridCache cache,
    ILogger<SetContactOrderHandler> logger) : ICommandHandler<SetContactOrder>
{
    public async Task HandleAsync(SetContactOrder command, CancellationToken cancellationToken = default)
    {
        var contact = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new ContactNotFoundException(command.Id);

        try
        {
            await repository.SetOrderAsync(contact.Id, command.Order, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Contact", contact.Id);
        }

        await cache.RemoveByTagAsync("contacts", cancellationToken);
        logger.LogInformation("Contact {Id} display order set to {Order}", contact.Id, command.Order);
    }
}