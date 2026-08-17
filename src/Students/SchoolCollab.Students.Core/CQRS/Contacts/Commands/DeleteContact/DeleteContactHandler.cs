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

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.DeleteContact;

public sealed class DeleteContactHandler(
    IContactRepository repository,
    StudentsDbContext db,
    ITenantProvider tenantProvider,
    ContactAuditor auditor,
    HybridCache cache,
    ILogger<DeleteContactHandler> logger) : ICommandHandler<DeleteContact>
{
    public async Task HandleAsync(DeleteContact command, CancellationToken cancellationToken = default)
    {
        var contact = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new ContactNotFoundException(command.Id);

        contact.SoftDelete();

        var tenantId = tenantProvider.GetTenantContext().TenantId;
        auditor.Record(
            db,
            tenantId,
            contact,
            ContactChangeKind.Deleted,
            command.Reason);

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
