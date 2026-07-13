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
        }

        var target = await Db.Contacts.FirstAsync(c => c.Id == contactId, cancellationToken);
        target.SetPrimary(true);

        await Db.SaveChangesAsync(cancellationToken);
    }
}
