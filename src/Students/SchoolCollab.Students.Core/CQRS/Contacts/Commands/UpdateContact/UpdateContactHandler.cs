using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.UpdateContact;

public sealed class UpdateContactHandler(
    IContactRepository repository,
    StudentsDbContext db,
    ITenantProvider tenantProvider,
    ContactAuditor auditor,
    HybridCache cache,
    ILogger<UpdateContactHandler> logger) : ICommandHandler<UpdateContact>
{
    public async Task HandleAsync(UpdateContact command, CancellationToken cancellationToken = default)
    {
        var contact = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new ContactNotFoundException(command.Id);

        var tenantId = tenantProvider.GetTenantContext().TenantId;

        // Record the audit row BEFORE mutating the contact in place so the
        // previous-* columns capture the pre-change values.
        auditor.Record(
            db,
            tenantId,
            contact,
            ContactChangeKind.Updated,
            command.Reason,
            newChannel: contact.Channel,
            newValue: command.Value,
            newLabel: command.Label,
            newCountryCode: command.CountryCode);

        contact.Update(command.Value, command.Label, command.CountryCode);

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
