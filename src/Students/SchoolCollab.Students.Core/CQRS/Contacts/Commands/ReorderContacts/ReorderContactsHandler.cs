using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.ReorderContacts;

public sealed class ReorderContactsHandler(
    IContactRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<ReorderContactsHandler> logger) : ICommandHandler<ReorderContacts>
{
    public async Task HandleAsync(ReorderContacts command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(ReorderContacts), typeof(Contact));

        try
        {
            await repository.ReorderAsync(
                command.OwnerType,
                command.OwnerId,
                command.OrderedContactIds,
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Contact", command.OwnerId);
        }

        await cache.RemoveByTagAsync("contacts", cancellationToken);
        logger.LogInformation(
            "Reordered {Count} contacts for {OwnerType} {OwnerId}",
            command.OrderedContactIds.Count, command.OwnerType, command.OwnerId);
    }
}