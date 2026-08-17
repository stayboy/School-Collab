using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Services;

/// <summary>
/// Writes a <see cref="ContactAuditEntry"/> into the supplied <see cref="StudentsDbContext"/>
/// so the audit row is persisted in the same transaction as the contact mutation
/// (the handler calls <see cref="StudentsDbContext.SaveChangesAsync"/> via the
/// repository after both the mutation and the audit row are tracked). Append-only:
/// never updates or deletes.
/// </summary>
public sealed class ContactAuditor(IActorAccessor actorAccessor)
{
    public void Record(
        StudentsDbContext db,
        Guid tenantId,
        Contact contact,
        ContactChangeKind changeKind,
        string reason,
        ContactChannel? newChannel = null,
        string? newValue = null,
        string? newLabel = null,
        string? newCountryCode = null)
    {
        db.ContactAuditEntries.Add(ContactAuditEntry.Create(
            tenantId: tenantId,
            contactId: contact.Id,
            ownerType: contact.OwnerType,
            ownerId: contact.OwnerId,
            changeKind: changeKind,
            previousChannel: contact.Channel,
            previousValue: contact.Value,
            previousLabel: contact.Label,
            previousCountryCode: contact.CountryCode,
            newChannel: newChannel,
            newValue: newValue,
            newLabel: newLabel,
            newCountryCode: newCountryCode,
            reason: reason,
            actorId: actorAccessor.ActorId,
            actorDisplayName: actorAccessor.ActorDisplayName));
    }
}
