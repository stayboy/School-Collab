using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.AddContact;

public sealed class AddContactHandler(
    IContactRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<AddContactHandler> logger) : ICommandHandler<AddContact, Guid>
{
    public async Task<Guid> HandleAsync(AddContact command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(AddContact), typeof(Contact));

        var contact = Contact.Create(
                command.OwnerType,
                command.OwnerId,
                command.Channel,
                command.Value,
                command.Label,
                command.CountryCode,
                command.DisplayOrder)
            .WithTenant(tenantProvider);

        await repository.AddAsync(contact, cancellationToken);
        await cache.RemoveByTagAsync("contacts", cancellationToken);

        logger.LogInformation("Contact {Id} added for {OwnerType} {OwnerId}", contact.Id, command.OwnerType, command.OwnerId);
        return contact.Id;
    }
}
