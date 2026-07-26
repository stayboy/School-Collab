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
    /// <summary>Sets a single contact's <c>DisplayOrder</c> (spec §4.9).</summary>
    Task SetOrderAsync(Guid contactId, int order, CancellationToken cancellationToken = default);
    /// <summary>
    /// Reorders an owner's contacts atomically. <paramref name="orderedIds"/>
    /// lists every non-deleted contact id for the owner in the desired order;
    /// the first id is assigned DisplayOrder 0, the second 1, and so on.
    /// Contacts omitted from the list keep their relative order at the tail.
    /// </summary>
    Task ReorderAsync(ContactOwnerType ownerType, Guid ownerId, IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default);
}
