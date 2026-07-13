using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.Subscribe;

public sealed class SubscribeHandler(
    IContactRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<SubscribeHandler> logger) : ICommandHandler<Subscribe>
{
    public async Task HandleAsync(Subscribe command, CancellationToken cancellationToken = default)
    {
        if (await repository.GetAsync(command.ContactId, cancellationToken) is null)
            throw new ContactNotFoundException(command.ContactId);

        var subscription = await repository.GetSubscriptionAsync(command.ContactId, command.Scope, command.ScopeRefId, cancellationToken);
        if (subscription is null)
        {
            subscription = ContactSubscription.Create(command.ContactId, command.Scope, command.ScopeRefId)
                .WithTenant(tenantProvider);
            subscription.Subscribe();
            await repository.AddSubscriptionAsync(subscription, cancellationToken);
        }
        else
        {
            subscription.Subscribe();
            await repository.UpdateSubscriptionAsync(subscription, cancellationToken);
        }

        await cache.RemoveByTagAsync("contacts", cancellationToken);
        logger.LogInformation("Contact {ContactId} subscribed to {Scope}", command.ContactId, command.Scope);
    }
}
