using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListContactAuditEntries;

public sealed class ListContactAuditEntriesHandler(StudentsDbContext db)
    : IQueryHandler<ListContactAuditEntries, ContactAuditEntryDto[]>
{
    public async Task<ContactAuditEntryDto[]> HandleAsync(ListContactAuditEntries query, CancellationToken ct = default)
    {
        var entries = await db.ContactAuditEntries.AsNoTracking()
            .Where(e => query.ContactId == null || e.ContactId == query.ContactId)
            .Where(e => query.OwnerType == null || e.OwnerType == query.OwnerType)
            .Where(e => query.OwnerId == null || e.OwnerId == query.OwnerId)
            .OrderByDescending(e => e.OccurredAt)
            .Skip(query.Skip).Take(query.Take)
            .ToArrayAsync(ct);

        return entries.Select(e => new ContactAuditEntryDto(
            e.Id,
            e.ContactId,
            e.ChangeKind.ToString(),
            e.PreviousChannel.ToString(),
            e.PreviousValue,
            e.PreviousLabel,
            e.PreviousCountryCode,
            e.NewChannel?.ToString(),
            e.NewValue,
            e.NewLabel,
            e.NewCountryCode,
            e.Reason,
            e.ActorId,
            e.ActorDisplayName,
            e.OccurredAt)).ToArray();
    }
}
