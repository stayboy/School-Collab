using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface IContactRepository
{
    Task<Contact?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Contact contact, CancellationToken cancellationToken = default);
    Task UpdateAsync(Contact contact, CancellationToken cancellationToken = default);
    Task<ContactSubscription?> GetSubscriptionAsync(Guid contactId, SubscriptionScope scope, Guid? scopeRefId, CancellationToken cancellationToken = default);
    Task AddSubscriptionAsync(ContactSubscription subscription, CancellationToken cancellationToken = default);
    Task UpdateSubscriptionAsync(ContactSubscription subscription, CancellationToken cancellationToken = default);
    Task SetPrimaryAsync(Guid contactId, ContactOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default);
}
