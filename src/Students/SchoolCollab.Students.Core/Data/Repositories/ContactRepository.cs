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

    public async Task SetPrimaryAsync(Guid contactId, ContactOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default)
    {
        var siblings = await Db.Contacts
            .Where(c => c.OwnerType == ownerType && c.OwnerId == ownerId && c.Id != contactId)
            .ToArrayAsync(cancellationToken);

        foreach (var sibling in siblings)
        {
            sibling.SetPrimary(false);
            // Spec §4.9 additive phase: bump siblings to a high order so the
            // designated contact becomes the lowest-DisplayOrder contact.
            sibling.SetDisplayOrder(int.MaxValue);
        }

        var target = await Db.Contacts.FirstAsync(c => c.Id == contactId, cancellationToken);
        target.SetPrimary(true);
        target.SetDisplayOrder(0);

        await Db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetOrderAsync(Guid contactId, int order, CancellationToken cancellationToken = default)
    {
        var target = await Db.Contacts.FirstAsync(c => c.Id == contactId, cancellationToken);
        target.SetDisplayOrder(order);
        // Keep IsPrimary in sync for the additive phase.
        target.SetPrimary(order == 0);
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
            combined[i].SetPrimary(i == 0);
        }

        await Db.SaveChangesAsync(cancellationToken);
    }
}
