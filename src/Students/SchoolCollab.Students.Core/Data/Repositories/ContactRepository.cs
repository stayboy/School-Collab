using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class ContactRepository(StudentsDbContext db)
    : SoftDeletableRepositoryBase<Contact, StudentsDbContext>(db), IContactRepository
{
    public Task<ContactSubscription?> GetSubscriptionAsync(Guid contactId, SubscriptionScope scope, Guid? scopeRefId, CancellationToken cancellationToken = default) =>
        Db.ContactSubscriptions
            .FirstOrDefaultAsync(s => s.ContactId == contactId && s.Scope == scope && s.ScopeRefId == scopeRefId, cancellationToken);

    public Task AddSubscriptionAsync(ContactSubscription subscription, CancellationToken cancellationToken = default)
    {
        Db.ContactSubscriptions.Add(subscription);
        return Db.SaveChangesAsync(cancellationToken);
    }

    public Task UpdateSubscriptionAsync(ContactSubscription subscription, CancellationToken cancellationToken = default) =>
        Db.SaveChangesAsync(cancellationToken);

    public async Task SetOrderAsync(Guid contactId, int order, CancellationToken cancellationToken = default)
    {
        var target = await Db.Contacts.FirstAsync(c => c.Id == contactId, cancellationToken);

        // Spec §4.9: setting a contact's order is a "move to position"
        // operation — the owner's other contacts shift to fill the gap,
        // preserving their relative order. Renumber the owner's full
        // contact list 0..n-1 with the target inserted at the new position.
        var ownerContacts = await Db.Contacts
            .Where(c => c.OwnerType == target.OwnerType && c.OwnerId == target.OwnerId && !c.IsDeleted)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(cancellationToken);

        var others = ownerContacts.Where(c => c.Id != target.Id).ToList();
        var insertAt = Math.Clamp(order, 0, others.Count);
        others.Insert(insertAt, target);
        for (var i = 0; i < others.Count; i++)
        {
            others[i].SetDisplayOrder(i);
        }

        await Db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReorderAsync(ContactOwnerType ownerType, Guid ownerId, IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default)
    {
        // Fetch the owner's contacts in a single round-trip; the supplied
        // orderedIds list defines the new order. Any contact omitted from
        // orderedIds keeps its relative order at the tail.
        var ownerContacts = await Db.Contacts
            .Where(c => c.OwnerType == ownerType && c.OwnerId == ownerId && !c.IsDeleted)
            .ToListAsync(cancellationToken);

        var orderedSet = orderedIds.ToHashSet();
        var ordered = orderedIds
            .Select(id => ownerContacts.FirstOrDefault(c => c.Id == id))
            .Where(c => c is not null)
            .Cast<Contact>()
            .ToList();
        var tail = ownerContacts
            .Where(c => !orderedSet.Contains(c.Id))
            .ToList();

        var combined = ordered.Concat(tail).ToList();
        for (var i = 0; i < combined.Count; i++)
        {
            combined[i].SetDisplayOrder(i);
        }

        await Db.SaveChangesAsync(cancellationToken);
    }
}
