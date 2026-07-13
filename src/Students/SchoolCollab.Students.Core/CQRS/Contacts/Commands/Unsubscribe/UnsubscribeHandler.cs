using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.Unsubscribe;

public sealed class UnsubscribeHandler(
    IContactRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<UnsubscribeHandler> logger) : ICommandHandler<Unsubscribe>
{
    public async Task HandleAsync(Unsubscribe command, CancellationToken cancellationToken = default)
    {
        if (await repository.GetAsync(command.ContactId, cancellationToken) is null)
            throw new ContactNotFoundException(command.ContactId);

        var subscription = await repository.GetSubscriptionAsync(command.ContactId, command.Scope, command.ScopeRefId, cancellationToken);
        if (subscription is null)
        {
            // Idempotent: ensure an explicit unsubscribed row exists.
            subscription = ContactSubscription.Create(command.ContactId, command.Scope, command.ScopeRefId)
                .WithTenant(tenantProvider);
            await repository.AddSubscriptionAsync(subscription, cancellationToken);
        }
        else
        {
            subscription.Unsubscribe();
            await repository.UpdateSubscriptionAsync(subscription, cancellationToken);
        }

        await cache.RemoveByTagAsync("contacts", cancellationToken);
        logger.LogInformation("Contact {ContactId} unsubscribed from {Scope}", command.ContactId, command.Scope);
    }
}
